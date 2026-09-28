export const kinds = ['STR', 'CON', 'AGI', 'DEF'];
export const kindNames = ['힘', '체력', '민첩', '방어'];
// CombatSkillRules.TrySelect and the project's JobSkillData assets, 2026-09-25.
export const skills = {
  str: {
    name: '광폭',
    icon: 'skill-str',
    cooldown: 40,
    cast: 0.7,
    description: '일정 시간 동안 공격 시 체력을 흡수합니다.',
  },
  agi: {
    name: '독 바르기',
    icon: 'skill-agi',
    cooldown: 30,
    cast: 1,
    description: '무기에 독을 발라 지속 피해를 줍니다.',
  },
  con: {
    name: '발차기',
    icon: 'skill-con',
    cooldown: 10,
    cast: 0.3,
    description: '앞의 적을 밀어내고 이동을 늦춥니다.',
  },
  def: {
    name: '도발',
    icon: 'skill-def',
    cooldown: 30,
    cast: 0.8,
    description: '도발을 준비해 적의 공격에 대응합니다.',
  },
  dash: {
    name: '대쉬',
    icon: 'skill-dash',
    cooldown: 8,
    cast: 0.35,
    description: '이동 방향으로 빠르게 회피합니다.',
  },
  roll: {
    name: '구르기',
    icon: 'skill-dash',
    cooldown: 10,
    cast: 0.35,
    description: '이동 방향으로 구릅니다.',
  },
  preset: {
    name: '프리셋 변경',
    icon: 'skill-preset',
    cooldown: 40,
    cast: 1.2,
    description: '다음 저장 프리셋으로 능력치를 바꿉니다.',
  },
  weapon: {
    name: '무기 스왑',
    icon: 'skill-weapon',
    cooldown: 10,
    cast: 0.75,
    description: '검과 활을 전환합니다.',
  },
};
export const jobs = {
  strategist: {
    title: '전략가',
    icon: 'Strategist',
    slots: ['dash', 'preset'],
    description: '주 능력치에 투자하고 프리셋을 바꿔 싸웁니다.',
  },
  polymath: {
    title: '폴리매스',
    icon: 'Polymath',
    slots: ['roll', 'weapon'],
    description: '균형 잡힌 능력치로 검과 활을 함께 사용합니다.',
  },
  str: {
    title: '모노스탯 · 힘',
    icon: 'Monostat_STR',
    slots: ['str'],
    description: '힘에 30포인트를 집중합니다.',
  },
  con: {
    title: '모노스탯 · 체력',
    icon: 'Monostat_CON',
    slots: ['con'],
    description: '체력에 30포인트를 집중합니다.',
  },
  agi: {
    title: '모노스탯 · 민첩',
    icon: 'Monostat_AGI',
    slots: ['agi'],
    description: '민첩에 30포인트를 집중합니다.',
  },
  def: {
    title: '모노스탯 · 방어',
    icon: 'Monostat_DEF',
    slots: ['def'],
    description: '방어에 30포인트를 집중합니다.',
  },
};
export function identity(values) {
  const max = Math.max(...values),
    min = Math.min(...values);
  if (max >= 30) return kinds[values.indexOf(max)].toLowerCase();
  return max - min <= 7 ? 'polymath' : 'strategist';
}
export const samplePeople = [
  { name: 'JW', job: '전략가', me: true },
  { name: 'LUMEN', job: '민첩', host: true },
  { name: 'NOA', job: '폴리매스' },
  { name: 'RAVEN', job: '힘' },
  { name: 'KAI', job: '방어' },
  { name: 'VOLT', job: '체력' },
  { name: 'ASH', job: '전략가' },
  { name: 'ZERO', job: '폴리매스' },
];
export const defaults = {
  quality: 'medium',
  brightness: 100,
  fps: 60,
  uiScale: 100,
  hudOpacity: 100,
  motion: true,
  master: 65,
  music: 45,
  sfx: 70,
  ui: 45,
  mute: false,
  backgroundMute: true,
  sensitivity: 100,
  crosshair: '#f4f2e8',
  keys: { skill1: 'KeyQ', skill2: 'KeyE' },
};
export const clone = (v) => JSON.parse(JSON.stringify(v));
export function loadSettings() {
  try {
    const saved = JSON.parse(localStorage.getItem('battle-pvp-preview-settings-v2') || 'null');
    if (!saved) return clone(defaults);
    const out = clone(defaults);
    for (const key of ['brightness', 'uiScale', 'hudOpacity', 'master', 'music', 'sfx', 'ui', 'sensitivity'])
      if (Number.isFinite(saved[key]))
        out[key] = Math.max(
          key === 'brightness' ? 60 : key === 'uiScale' ? 85 : key === 'sensitivity' ? 25 : 0,
          Math.min(
            key === 'sensitivity' ? 200 : key === 'brightness' ? 140 : key === 'uiScale' ? 115 : 100,
            saved[key],
          ),
        );
    if (['low', 'medium', 'high'].includes(saved.quality)) out.quality = saved.quality;
    if ([30, 60].includes(saved.fps)) out.fps = saved.fps;
    for (const key of ['motion', 'mute', 'backgroundMute'])
      if (typeof saved[key] === 'boolean') out[key] = saved[key];
    if (['#f4f2e8', '#88d3cb', '#e6bf7a'].includes(saved.crosshair)) out.crosshair = saved.crosshair;
    const k = saved.keys;
    if (k && validKey(k.skill1) && validKey(k.skill2) && k.skill1 !== k.skill2) out.keys = k;
    return out;
  } catch {
    return clone(defaults);
  }
}
export function validKey(code) {
  return /^(Key[BEFGHIJKLMNOPQRUVXYZ]|Digit[1-9])$/.test(code);
}
export function keyLabel(code) {
  return code?.replace('Key', '').replace('Digit', '') || '';
}
export class PreviewAudio {
  constructor(audio) {
    this.audio = audio;
    this.settings = clone(defaults);
    this.started = false;
    document.addEventListener('visibilitychange', () => this.apply(this.settings));
  }
  apply(s) {
    this.settings = clone(s);
    this.audio.volume = (s.master * s.music) / 10000;
    this.audio.muted = s.mute || (document.hidden && s.backgroundMute);
  }
  async start() {
    try {
      await this.audio.play();
      this.started = true;
      return true;
    } catch {
      return false;
    }
  }
  stop() {
    this.audio.pause();
    this.started = false;
  }
  async tone(kind = 'ui') {
    const s = this.settings;
    if (s.mute || !s.master || !s[kind] || (document.hidden && s.backgroundMute)) return;
    const AudioContext = window.AudioContext || window.webkitAudioContext;
    if (!AudioContext) return;
    this.context ??= new AudioContext();
    await this.context.resume();
    if (kind === 'ui') {
      try {
        this.uiBuffer ??= fetch('assets/audio/ui-terminal-click.wav')
          .then((r) => {
            if (!r.ok) throw new Error('UI audio unavailable');
            return r.arrayBuffer();
          })
          .then((bytes) => this.context.decodeAudioData(bytes));
        const buffer = await this.uiBuffer;
        const current = this.settings;
        if (current.mute || (document.hidden && current.backgroundMute)) return;
        this.uiSource?.stop();
        const source = this.context.createBufferSource(),
          gain = this.context.createGain();
        source.buffer = buffer;
        gain.gain.value = (current.master * current.ui) / 10000;
        source.connect(gain);
        gain.connect(this.context.destination);
        this.uiSource = source;
        source.onended = () => {
          source.disconnect();
          gain.disconnect();
          if (this.uiSource === source) this.uiSource = null;
        };
        source.start();
      } catch {
        this.uiBuffer = null;
      }
      return;
    }
    const osc = this.context.createOscillator(),
      gain = this.context.createGain(),
      now = this.context.currentTime;
    osc.type = 'triangle';
    osc.frequency.setValueAtTime(240, now);
    osc.frequency.exponentialRampToValueAtTime(90, now + 0.12);
    gain.gain.setValueAtTime(((s.master * s[kind]) / 10000) * 0.15, now);
    gain.gain.exponentialRampToValueAtTime(0.001, now + 0.22);
    osc.connect(gain);
    gain.connect(this.context.destination);
    osc.start(now);
    osc.stop(now + 0.24);
    osc.onended = () => {
      osc.disconnect();
      gain.disconnect();
    };
  }
}
