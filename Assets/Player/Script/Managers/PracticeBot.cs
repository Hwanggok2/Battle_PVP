using BattlePvp.Combat;
using BattlePvp.Stats;
using BattlePvp.UI;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.AI;

namespace BattlePvp.Networking
{
    /// <summary>Server-only steering; movement, damage, crowd control and revival use player rules.</summary>
    [RequireComponent(typeof(PlayerManager))]
    public sealed class PracticeBot : MonoBehaviour
    {
        private PlayerManager _movement;
        private PlayerCombat _combat;
        private HealthSystem _health;
        private HealthSystem _target;
        private NetworkIdentity _identity;
        private NavMeshPath _path;
        private readonly Vector3[] _corners = new Vector3[64];
        private int _corner, _cornerCount;
        private float _nextThink;
        private Vector3 _lastPosition;
        private float _stuckSince;
        private SkillLoadout _skills;
        private ExpandedSkillController _expanded;
        private CharacterController _targetBody;
        private float _nextSkill;
        private bool _targetVisible;
        private CharacterController _body;
        private float _nextFootwork;
        private float _strafeSign = 1f;

        private void Awake()
        {
            _path = new NavMeshPath();
            _movement = GetComponent<PlayerManager>();
            _combat = GetComponent<PlayerCombat>();
            _health = GetComponent<HealthSystem>();
            _identity = GetComponent<NetworkIdentity>();
            _skills = GetComponent<SkillLoadout>();
            _expanded = GetComponent<ExpandedSkillController>();
            _body = GetComponent<CharacterController>();
            _nextThink = Time.time + Random.value * .5f;
            _lastPosition = transform.position;
        }

        internal bool InitializeLoadout(PracticeBotSetup setup)
        {
            return _identity.isServer && setup != null && setup.IsValid &&
                GetComponent<BattlePvp.Characters.PlayerAppearance>().TrySelectOnServer(setup.CharacterId, true, out _) &&
                GetComponent<StatManager>().TryApplyServerPreset(setup.Stats) &&
                _skills.InitializeServerChoices(setup.Skills) && GetComponent<WeaponLoadout>().TrySelect(setup.Weapon, true);
        }

        public static StatContainer RandomStats()
        {
            int job = Random.Range(0, JobGuideContent.Count);
            if (job == 5) return BattleNetworkManager.DefaultPracticeStats();
            var points = new int[4];
            if (job < 4) points[job] = 30;
            else
            {
                for (int i = 0; i < 4; i++) points[i] = 4;
                points[Random.Range(0, 4)] = 18;
            }
            return new StatContainer { STR = new StatSlot { Invested = points[0] }, CON = new StatSlot { Invested = points[1] },
                AGI = new StatSlot { Invested = points[2] }, DEF = new StatSlot { Invested = points[3] } };
        }

        public static int[] RandomSkills()
        {
            var choices = SkillLoadout.Defaults();
            var candidates = new List<int>();
            for (int job = 0; job < JobGuideContent.Count; job++)
            {
                candidates.Clear();
                if (SkillGameData.Instance != null)
                {
                    foreach (var row in SkillGameData.Instance.Pools)
                        if (row.Job == job && !candidates.Contains(row.Kind)) candidates.Add(row.Kind);
                }
                else foreach (JobSkillKind kind in System.Enum.GetValues(typeof(JobSkillKind)))
                    if (CombatSkillRules.Allows(JobGuideContent.IdentityAt(job), kind)) candidates.Add((int)kind);
                if (candidates.Count < 2) continue;
                for (int slot = 0; slot < 2; slot++)
                {
                    int selected = Random.Range(0, candidates.Count);
                    choices[job * 2 + slot] = candidates[selected];
                    candidates.RemoveAt(selected);
                }
            }
            return choices;
        }

        private void Update()
        {
            if (!_identity.isServer || !(NetworkManager.singleton is BattleNetworkManager manager) || !manager.IsPractice) return;
            var state = BattleStateMachine.Instance;
            if (state == null || state.IsLoading || state.CurrentState != BattleState.InBattle || _health.IsDead)
            {
                _movement.ServerSetPracticeSteering(Vector3.zero, transform.forward);
                _combat.ServerPracticeBowAttack(transform.forward, false);
                if (_health.IsDead && state != null && state.CurrentState == BattleState.InBattle && Time.time >= _nextThink)
                { _nextThink = Time.time + .5f; _health.RequestRevive(); _cornerCount = 0; }
                return;
            }
            if (Time.time >= _nextThink)
            {
                _nextThink = Time.time + .55f + (_identity.netId % 4) * .04f;
                SelectTargetAndPath();
            }
            if (_target == null || _target.IsDead)
            {
                _movement.ServerSetPracticeSteering(WalkableSteering(Separation()), transform.forward);
                _combat.ServerPracticeBowAttack(transform.forward, false);
                _expanded.TickPracticeAction(transform.forward, false);
                return;
            }
            Vector3 aim = (TargetPoint - _expanded.ProjectileOrigin).normalized;
            _combat.ServerSetPracticeAim(aim);
            Vector3 facing = _target.transform.position - transform.position;
            facing.y = 0f;
            float distance = facing.magnitude;
            Vector3 steering = Vector3.zero;
            while (_corner < _cornerCount)
            {
                steering = _corners[_corner] - transform.position; steering.y = 0f;
                if (steering.sqrMagnitude > .16f) break;
                _corner++;
            }
            if (_corner >= _cornerCount) steering = Vector3.zero;
            steering = steering.normalized;
            if (_targetVisible)
            {
                bool ranged = !_combat.MeleeEquipped;
                float preferred = ranged ? 5f : 1.4f;
                if (distance < (ranged ? 8f : 2.8f))
                {
                    if (Time.time >= _nextFootwork)
                    {
                        _nextFootwork = Time.time + Random.Range(1.1f, 2.3f);
                        _strafeSign = Random.value < .5f ? -1f : 1f;
                    }
                    Vector3 toward = distance > .01f ? facing / distance : transform.forward;
                    Vector3 side = Vector3.Cross(Vector3.up, toward) * _strafeSign;
                    // Keep moving through swings, closing in or backing out around effective range.
                    steering = toward * Mathf.Clamp((distance - preferred) * 1.5f, -1f, 1f) + side * .55f;
                }
            }
            _movement.ServerSetPracticeSteering(WalkableSteering(steering + Separation()), facing);
            if (_combat.IsServerTaunted) return;
            bool inSight = _targetVisible && Vector3.Dot(transform.forward, facing.normalized) > .65f;
            if (_expanded.TickPracticeAction(aim, inSight)) return;
            if (Time.time >= _nextSkill && _combat.CanBeginExpandedSkill)
            {
                _nextSkill = Time.time + Random.Range(.4f, .8f);
                if (TrySkill(distance, aim, inSight)) return;
            }
            if (!_combat.MeleeEquipped)
            { _combat.ServerPracticeBowAttack(aim, inSight && distance <= 18f); return; }
            if (distance <= 1.65f && Mathf.Abs(_target.transform.position.y - transform.position.y) < 1f)
                _combat.ServerPracticeAttack(aim);
        }

        private Vector3 TargetPoint => _targetBody != null ? _targetBody.bounds.center : _target.transform.position + Vector3.up;

        private Vector3 Separation()
        {
            Vector3 result = Vector3.zero;
            float ownRadius = _body != null ? _body.radius * transform.lossyScale.x : .4f;
            foreach (var score in ScoreSystem.ActiveScores)
            {
                if (score == null || score.gameObject == gameObject || score.gameObject.scene != gameObject.scene ||
                    !score.TryGetComponent<HealthSystem>(out var health) || health.IsDead) continue;
                Vector3 away = transform.position - score.transform.position;
                if (Mathf.Abs(away.y) > 1.5f) continue;
                away.y = 0;
                float distance = away.magnitude;
                var body = score.GetComponent<CharacterController>();
                float clearance = Mathf.Max(1.05f, ownRadius + (body != null ? body.radius * body.transform.lossyScale.x : .4f) + .2f);
                if (distance >= clearance) continue;
                if (distance < .01f)
                {
                    // Stable opposite directions even when two roots are exactly coincident.
                    uint otherId = score.netId;
                    float angle = ((_identity.netId + otherId) * 137.5f) * Mathf.Deg2Rad;
                    away = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * (_identity.netId < otherId ? 1f : -1f);
                }
                else away /= distance;
                result += away * (1f - distance / clearance) * 2.5f;
            }
            return result;
        }

        private Vector3 WalkableSteering(Vector3 desired)
        {
            desired = Vector3.ClampMagnitude(desired, 1f);
            if (desired.sqrMagnitude < .001f || !NavMesh.SamplePosition(transform.position, out var from, 1f, NavMesh.AllAreas)) return Vector3.zero;
            Vector3 ahead = from.position + desired.normalized * .7f;
            if (!NavMesh.Raycast(from.position, ahead, out var edge, NavMesh.AllAreas)) return desired;
            Vector3 along = Vector3.ProjectOnPlane(desired, edge.normal); along.y = 0;
            if (along.sqrMagnitude > .01f && !NavMesh.Raycast(from.position, from.position + along.normalized * .7f, out _, NavMesh.AllAreas)) return along;
            _nextFootwork = Time.time + .6f; _strafeSign = -_strafeSign;
            return Vector3.zero;
        }

        private bool TrySkill(float distance, Vector3 aim, bool inSight)
        {
            int first = Random.Range(0, 2);
            for (int i = 0; i < 2; i++)
            {
                int slot = (first + i) % 2;
                if (!_skills.Select(slot, out var kind)) continue;
                var phase = _combat.GetSkillHudState(slot).Phase;
                bool hasCopy = kind == JobSkillKind.Steal && _expanded.CopiedKind >= 0;
                if (phase != SkillHudPhase.Ready && !hasCopy) continue;
                var effect = hasCopy ? (JobSkillKind)_expanded.CopiedKind : kind;
                if (ShouldUse(effect, distance, inSight) && _combat.ServerPracticeUseSkill(slot, aim)) return true;
            }
            return false;
        }

        private bool ShouldUse(JobSkillKind kind, float distance, bool inSight)
        {
            if (kind == JobSkillKind.PolymathWeaponSwap)
                return _combat.MeleeEquipped ? inSight && distance > 3f && distance < 18f : distance < 2.5f || !inSight;
            if (kind == JobSkillKind.Recovery) return distance < 8f && _health.CurrentHp > _health.MaxHp * .35f && _health.CurrentHp < _health.MaxHp * .8f;
            if (kind == JobSkillKind.Trap) return distance > 2f && distance < 7f && _expanded.TryGetTrapPlacement(out _);
            if (!inSight) return false;
            switch (kind)
            {
                case JobSkillKind.Hook: return distance > 2f && distance < ExpandedSkillController.Value(kind, "Range", 6);
                case JobSkillKind.Knife: return distance < ExpandedSkillController.Value(kind, "Range", 12);
                case JobSkillKind.Steal: return distance < ExpandedSkillController.Value(kind, "Range", 4);
                case JobSkillKind.MonostatConKick: return distance < 1.8f;
                case JobSkillKind.StrategistRoll:
                case JobSkillKind.PolymathRoll: return distance > 2.5f && distance < 6f;
                case JobSkillKind.Charge: return distance > 3f && distance < 10f;
                case JobSkillKind.Stealth:
                case JobSkillKind.Bash:
                case JobSkillKind.Fortify:
                case JobSkillKind.Thorns:
                case JobSkillKind.MonostatDefTaunt: return distance < 3f;
                case JobSkillKind.Berserk: return distance < 5f && _health.CurrentHp > _health.MaxHp * .35f;
                default: return distance < 8f;
            }
        }

        private void SelectTargetAndPath()
        {
            _target = null;
            float best = float.MaxValue;
            foreach (var score in ScoreSystem.ActiveScores)
            {
                if (score == null || score.gameObject == gameObject || score.gameObject.scene != gameObject.scene ||
                    !score.TryGetComponent(out HealthSystem health) || health.IsDead) continue;
                float distance = (score.transform.position - transform.position).sqrMagnitude;
                if (distance >= best) continue;
                if (!NavMesh.SamplePosition(transform.position, out var from, 2f, NavMesh.AllAreas) ||
                    !NavMesh.SamplePosition(score.transform.position, out var to, 2f, NavMesh.AllAreas) ||
                    !NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, _path) ||
                    _path.status != NavMeshPathStatus.PathComplete) continue;
                best = distance;
                _target = health;
                _cornerCount = _path.GetCornersNonAlloc(_corners);
                _corner = _cornerCount > 1 ? 1 : 0;
            }
            if (_target == null) _cornerCount = 0;
            _targetBody = _target != null ? _target.GetComponent<CharacterController>() : null;
            _targetVisible = _target != null && CombatValidation.HasClearPath(_expanded.ProjectileOrigin, TargetPoint, transform, _target.transform);
            // Recover only when stranded away from a target (for example after knockback off a ledge).
            bool holdingRangedPosition = !_combat.MeleeEquipped && _targetVisible && best <= 25f;
            if ((transform.position - _lastPosition).sqrMagnitude > .1f || best < 4f || _combat.IsAttackActive ||
                _combat.IsAimingBow || _expanded.IsPlacingTrap || holdingRangedPosition)
                _stuckSince = Time.time;
            else if (Time.time - _stuckSince > 6f)
            { _movement.ServerTeleportToSpawn(); _stuckSince = Time.time; }
            _lastPosition = transform.position;
        }
    }
}
