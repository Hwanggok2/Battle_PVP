using System.Collections;
using BattlePvp.CameraLogic;
using UnityEngine;

namespace BattlePvp.UI
{
    /// <summary>생존 상태를 바꾸지 않고 애니메이션, 부활 안내, 모델 표시와 관전 시점만 표현한다.</summary>
    [DisallowMultipleComponent]
    public sealed class PlayerLifePresentation : MonoBehaviour
    {
        private static readonly int Speed = Animator.StringToHash("Speed");
        private static readonly int MoveX = Animator.StringToHash("MoveX");
        private static readonly int MoveY = Animator.StringToHash("MoveY");
        private static readonly int Die = Animator.StringToHash("Die");
        private static readonly int IsDead = Animator.StringToHash("IsDead");
        private static readonly int Movement = Animator.StringToHash("Movement");

        private Transform _player;
        private Animator _animator;
        private FollowCamera _camera;
        private PlayerModelVisibility _model;
        private string _respawnPrompt;
        private Color _overlayColor;
        private PlayerRespawnCountdown _countdown;
        private Coroutine _countdownRoutine;
        private bool _hasLocalPresentation;
        private bool _hasAnimationState;
        private bool _showingDeathAnimation;
        private bool _isSpectating;
        private float[] _livingLayerWeights;

        public void Initialize(Transform player, Animator animator, string respawnPrompt, Color overlayColor)
        {
            _player = player;
            _animator = animator;
            _model = new PlayerModelVisibility(player);
            _respawnPrompt = respawnPrompt;
            _overlayColor = overlayColor;
        }

        public void AttachCamera(FollowCamera camera) => _camera = camera;

        public void PlayDeathAnimation() => ApplyAnimation(true);
        public void PlayReviveAnimation() => ApplyAnimation(false);

        private void ApplyAnimation(bool dead, bool force = false)
        {
            if (!force && _hasAnimationState && _showingDeathAnimation == dead) return;
            if (_animator == null || _animator.runtimeAnimatorController == null || _animator.layerCount == 0) return;
            _hasAnimationState = true;
            _showingDeathAnimation = dead;
            _animator.speed = 1f;
            _animator.SetFloat(Speed, 0f);
            _animator.SetFloat(MoveX, 0f);
            _animator.SetFloat(MoveY, 0f);
            _animator.ResetTrigger(Die);
            _animator.SetBool(IsDead, dead);
            if (dead)
            {
                // Attack/crouch masks otherwise keep overriding the full-body death pose.
                if (_livingLayerWeights == null)
                {
                    _livingLayerWeights = new float[_animator.layerCount];
                    for (int layer = 1; layer < _livingLayerWeights.Length; layer++)
                        _livingLayerWeights[layer] = _animator.GetLayerWeight(layer);
                }
                for (int layer = 1; layer < _animator.layerCount; layer++) _animator.SetLayerWeight(layer, 0f);
                if (_animator.HasState(0, Die)) _animator.CrossFadeInFixedTime(Die, .08f, 0, 0f);
                else _animator.SetTrigger(Die);
            }
            else
            {
                RestoreLivingLayers();
                _animator.Play(Movement, 0, 0f);
                _animator.Update(0f);
            }
        }

        public void BeginLocalDeath(PlayerRespawnCountdown countdown)
        {
            _isSpectating = false;
            PlayDeathAnimation();
            StopCountdown();
            _hasLocalPresentation = true;
            _countdown = countdown;
            if (!isActiveAndEnabled) return;
            if (_countdown.IsReady(Time.timeAsDouble)) ShowRespawnPrompt();
            else _countdownRoutine = StartCoroutine(ShowCountdown());
        }

        private IEnumerator ShowCountdown()
        {
            int displayedSeconds = -1;
            while (!_countdown.IsReady(Time.timeAsDouble))
            {
                int seconds = _countdown.SecondsRemaining(Time.timeAsDouble);
                if (seconds != displayedSeconds)
                {
                    displayedSeconds = seconds;
                    PlayerHUD.UpdateLocalDeathOverlay(true, seconds.ToString(), _overlayColor);
                }
                yield return null;
            }
            ShowRespawnPrompt();
            _countdownRoutine = null;
        }

        private void ShowRespawnPrompt()
        {
            _model?.Hide();
            PlayerHUD.UpdateLocalDeathOverlay(true, _respawnPrompt, _overlayColor);
        }

        public void ShowLocalRevived()
        {
            _isSpectating = false;
            StopCountdown();
            _hasLocalPresentation = true;
            _model?.Restore();
            PlayReviveAnimation();
            PlayerHUD.UpdateLocalDeathOverlay(false);
            if (_camera != null)
            {
                _camera.SetTarget(_player);
                _camera.IsLocked = false;
            }
        }

        public void ShowMatchEnd(Transform winnerTarget, bool isWinner)
        {
            StopCountdown();
            _hasLocalPresentation = true;
            _isSpectating = !isWinner;
            _model?.Restore();
            ApplyAnimation(false, force: true);
            PlayerHUD.UpdateLocalDeathOverlay(false);
            if (_camera != null)
            {
                _camera.SetTarget(isWinner || winnerTarget == null ? _player : winnerTarget);
                _camera.IsLocked = !isWinner;
            }
        }

        public void RefreshSpectateTarget()
        {
            if (!isActiveAndEnabled || !_hasLocalPresentation || !_isSpectating ||
                _camera == null || _camera.Target != null || _player == null) return;

            // The winner can leave after the result RPC. Keep spectator input locked;
            // only the camera's missing target changes, never the frozen match result.
            _camera.SetTarget(_player);
        }

        public void Suspend()
        {
            RestoreLivingLayers();
            _isSpectating = false;
            StopCountdown();
            _model?.Restore();
            if (_hasLocalPresentation) PlayerHUD.UpdateLocalDeathOverlay(false);
            _hasLocalPresentation = false;
            _hasAnimationState = false;
        }

        private void RestoreLivingLayers()
        {
            if (_livingLayerWeights == null) return;
            if (_animator != null)
                for (int layer = 1; layer < Mathf.Min(_animator.layerCount, _livingLayerWeights.Length); layer++)
                    _animator.SetLayerWeight(layer, _livingLayerWeights[layer]);
            _livingLayerWeights = null;
        }

        private void StopCountdown()
        {
            if (_countdownRoutine != null) StopCoroutine(_countdownRoutine);
            _countdownRoutine = null;
        }

        private void OnDisable() => Suspend();
    }
}
