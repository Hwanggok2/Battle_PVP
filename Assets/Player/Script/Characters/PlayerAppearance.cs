using System;
using System.Collections;
using BattlePvp.Combat;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BattlePvp.Characters
{
    [DisallowMultipleComponent]
    public sealed class PlayerAppearance : NetworkBehaviour
    {
        [SerializeField] private SkinnedMeshRenderer _body;
        [SyncVar(hook = nameof(OnSelectedChanged))] private string _selectedId = CharacterCatalog.DefaultId;
        [SyncVar] private bool _serverSelectionReady = true;
        private CharacterSkin _skin;
        private bool _initialReceived;
        private double _initialDeadline, _nextRequest;
        private float _requestDeadline;
        private Coroutine _visualLoad;
        private int _visualRequestVersion;
        private bool _visualNeedsApply;
        public event Action Changed;
        public event Action<bool, string> RequestCompleted;
        public string SelectedId => _selectedId;
        public bool HasServerSelectionReady => _serverSelectionReady;
        public bool IsPending { get; private set; }
        public int VisualRevision { get; private set; }
        public static bool CanEdit => CanEditScene(SceneManager.GetActiveScene().name);
        public static bool CanEditScene(string scene) => scene == "Lobby" || scene == "Battle_waiting";
        private CharacterSkin Skin => _skin ??= new CharacterSkin(_body);

        private void Awake() { _skin = new CharacterSkin(_body); }
        private void OnEnable()
        {
            if (_visualNeedsApply) { _visualNeedsApply = false; ApplyVisual(); }
        }
        private void OnDestroy() { _visualRequestVersion++; _skin?.Dispose(); }
        private void Start()
        {
            if ((!NetworkClient.active && !NetworkServer.active))
                SetSelected(CharacterAppearanceStore.Read());
        }
        public override void OnStartServer()
        {
            _serverSelectionReady = false;
            _initialReceived = false;
            _initialDeadline = NetworkTime.time + 10;
        }
        public override void OnStartClient() => ApplyVisual();
        public override void OnStartLocalPlayer()
        {
            IsPending = true; _requestDeadline = Time.unscaledTime + 10;
            CmdSelect(CharacterAppearanceStore.Read(), true);
        }
        private void Update()
        {
            if (isServer) ExpireInitialSelection(NetworkTime.time);
            if (IsPending && Time.unscaledTime >= _requestDeadline)
            { IsPending = false; RequestCompleted?.Invoke(false, "응답을 받지 못했습니다. 다시 시도해 주세요."); }
        }
        private void OnDisable()
        {
            if (_visualLoad != null)
            {
                _visualRequestVersion++;
                StopCoroutine(_visualLoad); _visualLoad = null;
                _visualNeedsApply = true;
            }
            if (!IsPending) return;
            IsPending = false; RequestCompleted?.Invoke(false, "캐릭터 연결이 종료되었습니다.");
        }
        public bool Validate(CharacterDefinition definition, out string error) => Skin.Validate(definition, out error);
        public bool Request(string id, out string error)
        {
            error = null;
            if (!CanEdit) { error = "캐릭터는 로비와 대기실에서 변경할 수 있습니다."; return false; }
            if (IsPending) { error = "변경을 적용하고 있습니다."; return false; }
            if (GetComponent<HealthSystem>() is { IsDead: true }) { error = "부활 후 변경할 수 있습니다."; return false; }
            var definition = CharacterCatalog.Instance?.Find(id);
            if (!Skin.Validate(definition, out error)) return false;
            if (NetworkClient.active)
            {
                if (!isLocalPlayer) { error = "로컬 플레이어를 기다리고 있습니다."; return false; }
                IsPending = true; _requestDeadline = Time.unscaledTime + 10;
                CmdSelect(id, false);
            }
            else
            {
                SetSelected(id); CharacterAppearanceStore.Save(id);
                RequestCompleted?.Invoke(true, "캐릭터를 적용했습니다.");
            }
            return true;
        }
        [Command] private void CmdSelect(string id, bool initial)
        {
            bool accepted = TrySelectOnServer(id, initial, out string error);
            TargetResult(connectionToClient, accepted, _selectedId, error ?? "지금은 캐릭터를 변경할 수 없습니다.");
        }
        internal bool TrySelectOnServer(string id, bool initial, out string error)
        {
            ExpireInitialSelection(NetworkTime.time);
            bool initialAllowed = initial && !_initialReceived && NetworkTime.time <= _initialDeadline;
            error = null;
            bool accepted = NetworkTime.time >= _nextRequest && (initialAllowed || CanEditScene(gameObject.scene.name)) &&
                GetComponent<HealthSystem>() is not { IsDead: true };
            var definition = CharacterCatalog.Instance?.Find(id);
            if (accepted) accepted = Skin.Validate(definition, out error);
            _nextRequest = NetworkTime.time + .2;
            if (accepted)
            {
                SetSelected(id);
                _initialReceived = true;
                _serverSelectionReady = true;
            }
            return accepted;
        }
        internal void ExpireInitialSelection(double now)
        {
            if (_serverSelectionReady || now < _initialDeadline) return;
            SetSelected(CharacterCatalog.DefaultId);
            _initialReceived = true;
            _serverSelectionReady = true;
        }
        [TargetRpc] private void TargetResult(NetworkConnectionToClient target, bool accepted, string id, string error)
        {
            IsPending = false;
            if (accepted) { SetSelected(id); CharacterAppearanceStore.Save(id); }
            RequestCompleted?.Invoke(accepted, accepted ? "캐릭터를 적용했습니다." : error);
        }
        private void SetSelected(string id) { _selectedId = id; ApplyVisual(); }
        private void OnSelectedChanged(string oldId, string newId) => ApplyVisual();
        private void ApplyVisual()
        {
            int version = ++_visualRequestVersion;
            if (_visualLoad != null) { StopCoroutine(_visualLoad); _visualLoad = null; }
            var definition = CharacterCatalog.Instance?.Find(_selectedId);
            GetComponent<BattlePvp.Stats.StatManager>()?.RefreshCharacterStats();
            if (Application.isPlaying && !isActiveAndEnabled) { _visualNeedsApply = true; return; }
            if (Application.isPlaying && isActiveAndEnabled && definition != null && !definition.IsVisualLoaded)
            { _visualLoad = StartCoroutine(LoadVisual(definition, version)); return; }
            FinishVisual(definition);
        }
        private IEnumerator LoadVisual(CharacterDefinition definition, int version)
        {
            yield return definition.LoadVisualAsync();
            if (version != _visualRequestVersion) yield break;
            _visualLoad = null; FinishVisual(definition);
        }
        private void FinishVisual(CharacterDefinition definition)
        {
            if (!Skin.Apply(definition, out _)) Skin.Restore();
            VisualRevision++; Changed?.Invoke();
        }
    }

    public static class CharacterAppearanceStore
    {
        // Same account-scoped local preference boundary as SkillLoadoutStore.
        private static string Key => "BattlePvp.Character.v1." + (PlayFab.PlayFabSettings.staticPlayer.PlayFabId ?? "offline");
        public static string Read() => CharacterCatalog.Instance != null
            ? CharacterCatalog.Instance.ResolveId(PlayerPrefs.GetString(Key, CharacterCatalog.DefaultId)) : CharacterCatalog.DefaultId;
        public static void Save(string id)
        {
            if (CharacterCatalog.Instance?.Find(id) == null) return;
            PlayerPrefs.SetString(Key, id); PlayerPrefs.Save();
        }
    }
}
