import { GameWorld, mapInfo } from './world.js';
import {
  kinds,
  kindNames,
  skills,
  jobs,
  identity,
  samplePeople,
  defaults,
  clone,
  loadSettings,
  validKey,
  keyLabel,
  PreviewAudio,
} from './game-data.js';
const $ = (s, r = document) => r.querySelector(s),
  $$ = (s, r = document) => [...r.querySelectorAll(s)];
const game = $('#game'),
  screen = $('#screen'),
  modal = $('#modal'),
  audio = new PreviewAudio($('#bgm'));
const esc = (s) =>
  String(s).replace(
    /[&<>"']/g,
    (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c],
  );
const state = {
  screen: 'title',
  presets: [
    [18, 4, 6, 2],
    [30, 0, 0, 0],
    [8, 8, 7, 7],
  ],
  preset: 0,
  job: 'strategist',
  roomName: '가볍게 한 판',
  roster: clone(samplePeople),
  host: 'LUMEN',
  map: 'arena',
  viewMap: 'lobby',
  selected: 0,
  settings: loadSettings(),
  weapon: '검',
  cooldowns: {},
  casting: null,
  dead: false,
  chat: [
    { name: '', text: '대기실에 입장했습니다.' },
    { name: 'LUMEN', text: '모두 모이면 시작할게요.' },
  ],
};
const rooms = [
  { name: '가볍게 한 판', host: 'LUMEN', count: 5, map: 'arena' },
  { name: '자유 대전', host: 'RAVEN', count: 3, map: 'foundry' },
  { name: '연습 같이 하실 분', host: 'NOA', count: 7, map: 'arena' },
  { name: '저녁 한 판', host: 'VOLT', count: 8, map: 'foundry' },
];
let world,
  reviewOpen = false,
  settingsDraft = null,
  settingsTab = 'system',
  listeningKey = null,
  statDraft = null,
  draftPreset = 0,
  returnFocus = null,
  castTimer,
  toastTimer,
  flashTimer,
  deathUntil = 0,
  lastDirection = [0, -1];
function showWorldError(e) {
  console.error(e);
  const el = $('#world-error');
  el.hidden = false;
  el.textContent = '3D 화면을 불러오지 못했습니다. 하드웨어 가속을 지원하는 브라우저에서 다시 열어 주세요.';
}
try {
  world = new GameWorld($('#world-canvas'), $('#world-labels'));
  world
    .load()
    .then(() => applySettings(state.settings))
    .catch(showWorldError);
} catch (e) {
  showWorldError(e);
}
function me() {
  return { ...samplePeople[0], job: jobs[state.job].title, host: state.host === 'JW' };
}
function roster() {
  return state.roster.map((p) => (p.me ? me() : { ...p, host: p.name === state.host }));
}
function toast(text) {
  $('#toast').textContent = text;
  $('#toast').classList.add('show');
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => $('#toast').classList.remove('show'), 2600);
}
function applySettings(s) {
  audio.apply(s);
  world?.applySettings(s);
  document.documentElement.style.setProperty('--ui-scale', s.uiScale / 100);
  document.documentElement.style.setProperty('--hud-opacity', s.hudOpacity / 100);
  document.documentElement.style.setProperty('--crosshair', s.crosshair);
  document.documentElement.dataset.motion = s.motion ? 'reduced' : 'normal';
}
applySettings(state.settings);
function header() {
  const inRoom = ['waiting', 'battle', 'results'].includes(state.screen);
  return `<div class="row"><button class="brand" ${inRoom ? 'data-modal="leave"' : 'data-go="lobby"'}>BATTLE <span>PVP</span></button><div class="header-place">${state.screen === 'waiting' ? '출격 격납고' : state.screen === 'battle' ? mapInfo[state.map].name : state.screen === 'maps' ? '맵 둘러보기' : '작전실'}<small>${state.screen === 'waiting' ? esc(state.roomName) : ''}</small></div></div><div class="header-right">${inRoom ? `<div class="connection-count" aria-label="참가 인원"><b id="player-count">${state.roster.length}</b> / 8</div>` : ''}<span class="player-name">JW</span><button class="header-stats" data-modal="stats">스탯</button><button class="icon-button" data-modal="settings" aria-label="설정">⚙</button>${inRoom ? '<button class="icon-button" data-modal="pause" aria-label="메뉴">☰</button>' : ''}</div>`;
}
function title() {
  return '<section class="title-screen"><button class="title-button" data-go="login" aria-label="Battle PvP 시작"><h1>BATTLE<span>PVP</span></h1></button></section>';
}
function login() {
  return `<section class="login-card panel"><h1>BATTLE <span>PVP</span></h1><div class="divider"></div><form id="login-form"><label class="field">아이디<input name="username" autocomplete="off" placeholder="아이디" maxlength="32"></label><label class="field">비밀번호<input name="password" type="password" autocomplete="off" placeholder="비밀번호"></label><button type="submit" class="primary">로그인</button></form><div class="row spread" style="margin-top:15px"><button class="text-button" data-modal="signup">회원가입</button><button class="text-button" data-go="lobby">바로 둘러보기</button></div><small style="display:block;margin-top:20px">시안에서는 계정을 입력하지 않아도 됩니다.</small></section>`;
}
function lobby() {
  const j = jobs[identity(state.presets[state.preset])];
  return `<nav class="lobby-menu" aria-label="로비 메뉴"><button class="main-entry" data-go="rooms">전투 참가</button><button data-modal="stats">스탯 설정</button><button data-modal="character">캐릭터</button><button data-modal="settings">설정</button></nav><div class="map-caption"><small>LOBBY</small><h2>작전실</h2></div><aside class="profile-card"><div class="row"><img src="assets/${j.icon}.png" alt="${j.title}"><div><h3>JW</h3><small>${j.title} · 프리셋 ${state.preset + 1}</small></div></div><div class="mini-stats">${kinds.map((k, i) => `<span>${k}<b>${state.presets[state.preset][i] + 5}</b></span>`).join('')}</div></aside>`;
}
function roomList() {
  return `<section class="room-browser panel"><div class="row spread"><h1>방 목록</h1><button data-modal="create">방 만들기</button></div><div class="room-list">${rooms.map((r, i) => `<button class="room-item ${state.selected === i ? 'selected' : ''}" data-room="${i}" aria-pressed="${state.selected === i}"><div><h3>${esc(r.name)}</h3><small>${esc(r.host)} · ${r.count === 8 ? '진행 중' : '대기 중'}</small></div><span class="room-map muted">${mapInfo[r.map].name}</span><strong>${r.count}<small> / 8</small></strong></button>`).join('')}</div><div class="row spread"><button data-go="lobby">뒤로</button><div class="row"><button id="refresh-rooms" aria-label="방 목록 새로고침">↻</button><button id="join-room" class="primary" ${rooms[state.selected].count >= 8 ? 'disabled' : ''}>입장</button></div></div></section>`;
}
function chatPanel() {
  return `<section class="chat"><div class="chat-lines" id="chat-lines" role="log" aria-label="대기실 채팅">${state.chat
    .slice(-5)
    .map(
      (m) =>
        `<p class="${m.name ? '' : 'system'}">${m.name ? '<b>' + esc(m.name) + '</b>' : ''}${esc(m.text)}</p>`,
    )
    .join(
      '',
    )}</div><form id="chat-form"><input name="message" aria-label="채팅 메시지" placeholder="Enter · 채팅" maxlength="140" autocomplete="off"><button type="submit">전송</button></form></section>`;
}
function waiting() {
  return `<div class="waiting-info"><h1>${esc(state.roomName)}</h1><p>방장 ${esc(state.host)} · 개인전</p></div>${chatPanel()}<div class="waiting-footer"><div class="map-choice"><small>다음 전장</small><select id="room-map" aria-label="전장 선택" ${state.host === 'JW' ? '' : 'disabled'}>${['arena', 'foundry'].map((k) => `<option value="${k}" ${state.map === k ? 'selected' : ''}>${mapInfo[k].name}</option>`).join('')}</select></div><button data-modal="stats">스탯 설정</button><button class="primary" id="start-match" ${state.host === 'JW' ? '' : 'disabled'}>${state.host === 'JW' ? '경기 시작' : '방장 시작 대기'}</button><button class="text-button" data-modal="leave">방 나가기</button></div>`;
}
function skillSlots() {
  return jobs[state.job].slots
    .map(
      (id, i) =>
        `<button class="skill-button" data-skill="${i}" data-skill-id="${id}" aria-label="${skills[id].name} 사용"><kbd>${keyLabel(state.settings.keys['skill' + (i + 1)])}</kbd><img src="assets/${skills[id].icon}.png" alt=""><span class="skill-name">${skills[id].name}</span><span class="cd"></span><i class="cd-bar"></i></button>`,
    )
    .join('');
}
function battle() {
  return `<div class="battle-top"><div class="rank-strip">${roster()
    .map(
      (p, i) =>
        `<div class="rank-item ${p.me ? 'me' : ''}">${esc(p.name)}<b>${[8, 7, 6, 5, 4, 3, 2, 1][i]}</b></div>`,
    )
    .join(
      '',
    )}</div><div class="match-clock"><strong>03:42</strong><small>개인전</small></div></div><div class="crosshair" aria-hidden="true"></div><div class="skill-flash" id="skill-flash" role="status"></div><div id="battle-chat" hidden>${chatPanel()}</div><div class="touch-move" aria-label="이동"><button data-move="-1,0" aria-label="왼쪽 이동">←</button><button data-move="0,-1" aria-label="앞으로 이동">↑</button><button data-move="0,1" aria-label="뒤로 이동">↓</button><button data-move="1,0" aria-label="오른쪽 이동">→</button></div><div class="combat-controls" aria-label="전투 조작"><div class="skill-bar" id="skill-bar">${skillSlots()}</div><button id="basic-attack" class="attack-button" aria-label="기본 공격"><span class="attack-glyph" aria-hidden="true">╱</span><b>공격</b><small>클릭 / 터치</small></button></div><div class="battle-bottom"><div class="health"><div class="health-title"><strong>JW · ${jobs[state.job].title}</strong><span>780 / 1000</span></div><div class="health-bar"><i></i></div><small>프리셋 ${state.preset + 1}</small></div><div class="weapon"><small>현재 무기</small><strong id="weapon-name">${state.weapon}</strong></div></div>`;
}
function results() {
  return `<section class="results-panel panel"><div class="section-label">경기 종료</div><h1>1위 · JW</h1><table class="results-table"><thead><tr><th>순위</th><th>플레이어</th><th>처치</th><th>사망</th><th>가한 피해</th></tr></thead><tbody>${roster()
    .map(
      (p, i) =>
        `<tr class="${p.me ? 'mine' : ''}"><td>${i + 1}</td><td>${esc(p.name)}</td><td>${8 - i}</td><td>${i + 2}</td><td>${[4820, 4300, 3810, 3220, 2920, 2440, 2100, 1700][i]}</td></tr>`,
    )
    .join(
      '',
    )}</tbody></table><div class="button-row"><button data-modal="leave">방 나가기</button><button class="primary" data-go="waiting">대기실로</button></div></section>`;
}
function maps() {
  const info = mapInfo[state.viewMap];
  return `<section class="map-browser panel"><h1>${info.name}</h1><div class="map-options">${Object.entries(
    mapInfo,
  )
    .map(
      ([k, v]) =>
        `<button data-map="${k}" class="${state.viewMap === k ? 'active' : ''}">${v.name}<small>${v.kind}</small></button>`,
    )
    .join(
      '',
    )}</div><p class="map-description">${info.text}</p><div class="map-facts"><span>${info.size}</span><span>${info.kind}</span></div><div class="map-view-buttons"><button data-camera="overhead">평면 보기</button><button data-camera="closeup">바닥 가까이</button></div></section><aside class="materials panel"><h3>적용한 에셋</h3><div class="texture-row">${[
    ['alloy-floor', '금속 패널'],
    ['rooftop-floor', '옥상 콘크리트'],
    ['night-city-sky', '하늘'],
  ]
    .map(
      ([k, n]) =>
        `<button data-asset="${k}" aria-label="${n} 원본 보기"><img src="assets/${k}.png" alt="${n}"><small>${n}</small></button>`,
    )
    .join(
      '',
    )}</div><div class="material-switches"><button id="toggle-textures" class="${world?.flags.textures ? 'active' : ''}">바닥 ${world?.flags.textures ? 'ON' : 'OFF'}</button><button id="toggle-sky" class="${world?.flags.sky ? 'active' : ''}">하늘 ${world?.flags.sky ? 'ON' : 'OFF'}</button></div><p class="material-note">드래그로 회전 · 두 손가락으로 확대<br>브라우저 3D 시안 / Unity 적용 전<br><a href="assets/maps/${state.viewMap}.glb" download>맵 3D 구조 저장</a> · <a href="assets/maps/map-layouts.json" download>배치 정보</a></p></aside>`;
}
const views = { title, login, lobby, rooms: roomList, waiting, battle, results, maps };
function cancelCast() {
  clearTimeout(castTimer);
  state.casting = null;
}
function render(name = state.screen) {
  if (!views[name]) name = 'title';
  if (modal.open) modal.close();
  discardSettings();
  cancelCast();
  state.screen = name;
  if (name !== 'battle') state.dead = false;
  reviewOpen = false;
  game.dataset.screen = name;
  $('#world-canvas').setAttribute(
    'aria-label',
    name === 'battle'
      ? '전투 공간: 드래그로 시점, 클릭/터치로 공격, WASD 또는 화살표 버튼으로 이동'
      : '3D 공간: 드래그로 회전, 두 손가락으로 확대',
  );
  $('#game-header').innerHTML = name === 'title' ? '' : header();
  screen.innerHTML = views[name]();
  const map =
    name === 'maps'
      ? state.viewMap
      : ['battle', 'results'].includes(name)
        ? state.map
        : name === 'waiting'
          ? 'waiting'
          : 'lobby';
  world?.setMap(map, name);
  world?.setPlayers(['title', 'login', 'rooms'].includes(name) ? [] : map === 'lobby' ? [me()] : roster());
  world?.setInputEnabled(true);
  $('#world-tools').innerHTML = ['title', 'login', 'rooms', 'results'].includes(name)
    ? ''
    : `<div class="world-actions"><button data-camera="reset">시점 초기화</button>${name === 'maps' ? '' : '<button data-go="maps">맵 둘러보기</button>'}<small>${name === 'battle' ? '클릭 · 공격 / 드래그 · 시점 / WASD · 이동' : '드래그 · 회전 / 두 손가락 · 확대'}</small></div>`;
  renderReview();
  history.replaceState(null, '', '#' + name);
  syncCooldowns();
}
function renderReview() {
  let html = '';
  if (state.screen === 'waiting')
    html = `<div class="review-session"><small>입·퇴장 확인</small><button id="add-player" ${state.roster.length >= 8 ? 'disabled' : ''}>1명 입장</button><button id="remove-player" ${state.roster.length <= 1 ? 'disabled' : ''}>1명 퇴장</button><button id="host-switch" class="host-switch">${state.host === 'JW' ? '참가자로 보기' : '방장으로 보기'}</button></div>`;
  if (state.screen === 'battle')
    html = `<div class="review-session"><small>직업 확인</small><select id="preview-job" aria-label="미리보기 직업" style="padding:5px;font-size:12px;width:130px">${Object.entries(
      jobs,
    )
      .map(([k, j]) => `<option value="${k}" ${state.job === k ? 'selected' : ''}>${j.title}</option>`)
      .join(
        '',
      )}</select><button data-modal="death">사망</button><button data-go="results">결과</button></div>`;
  if (reviewOpen)
    html += `<aside class="review-drawer panel"><h3>화면 미리보기</h3><p>디자인과 조작을 확인하는 시안입니다. 실제 로그인·통신·전투 판정은 수행하지 않습니다.</p><div class="review-links">${[
      ['title', '시작 화면'],
      ['login', '로그인'],
      ['lobby', '로비'],
      ['rooms', '방 목록'],
      ['waiting', '대기실'],
      ['battle', '전투'],
      ['results', '결과'],
      ['maps', '맵 둘러보기'],
    ]
      .map(([k, n]) => `<button data-go="${k}">${n}</button>`)
      .join(
        '',
      )}<button data-modal="settings">설정</button><button data-modal="guide">반영 범위</button><button data-modal="connection">연결 중</button><button id="host-disconnect">호스트 종료</button></div><a class="details-link" href="IMPLEMENTATION.md" target="_blank" rel="noopener">구현 계획·완료 기준</a></aside>`;
  $('#review-controls').innerHTML = html;
  $('#review-toggle').setAttribute('aria-expanded', String(reviewOpen));
}
function modalHead(title, kicker = '', dismissible = true) {
  return `<div class="modal-head"><div>${kicker ? '<small>' + kicker + '</small>' : ''}<h2 id="modal-title">${title}</h2></div>${dismissible ? '<button data-close aria-label="닫기">×</button>' : ''}</div>`;
}
function settingRow(label, control, note = '') {
  return `<div class="setting"><label>${label}${note ? '<small>' + note + '</small>' : ''}</label><div class="setting-control">${control}</div></div>`;
}
function slider(key, label, min = 0, max = 100, suffix = '%') {
  const v = settingsDraft[key];
  return settingRow(
    label,
    `<input aria-label="${label}" data-setting="${key}" type="range" min="${min}" max="${max}" value="${v}"><output data-output="${key}">${v}${suffix}</output>`,
  );
}
function choice(key, label, options) {
  return settingRow(
    label,
    `<select aria-label="${label}" data-setting="${key}">${options.map(([v, n]) => `<option value="${v}" ${String(settingsDraft[key]) === String(v) ? 'selected' : ''}>${n}</option>`).join('')}</select>`,
  );
}
function toggle(key, label, note = '') {
  return settingRow(
    label,
    `<input type="checkbox" aria-label="${label}" data-setting="${key}" ${settingsDraft[key] ? 'checked' : ''}>`,
    note,
  );
}
function settingsPanel() {
  let body = '';
  if (settingsTab === 'system')
    body = `<h3>화면</h3>${choice('quality', '그래픽 품질', [
      ['low', '낮음'],
      ['medium', '보통'],
      ['high', '높음'],
    ])}${choice('fps', '최대 프레임', [
      [30, '30 FPS'],
      [60, '60 FPS'],
    ])}${slider('brightness', '밝기', 60, 140)}${slider('uiScale', 'HUD 크기', 85, 115)}${slider('hudOpacity', 'HUD 불투명도', 35, 100)}${toggle('motion', '배경 움직임 줄이기')}${settingRow('전체화면', '<button id="fullscreen" style="padding:8px 12px">전환</button>')}`;
  if (settingsTab === 'sound')
    body = `<h3>음량</h3>${slider('master', '전체 음량')}${slider('music', '배경 음악')}${slider('sfx', '효과음')}${slider('ui', 'UI 소리')}${toggle('mute', '모든 소리 끄기')}${toggle('backgroundMute', '다른 창 사용 시 음소거')}<div class="button-row" style="margin-top:18px"><button id="test-music">${audio.started ? '음악 정지' : '음악 듣기'}</button><button data-tone="sfx">효과음 듣기</button><button data-tone="ui">UI 소리 듣기</button></div>`;
  if (settingsTab === 'controls')
    body = `<h3>전투</h3>${['skill1', 'skill2'].map((key, i) => settingRow('스킬 ' + (i + 1), `<button data-rebind="${key}" class="rebind ${listeningKey === key ? 'listening' : ''}" aria-label="스킬 ${i + 1} 단축키 변경">${listeningKey === key ? '키 입력…' : keyLabel(settingsDraft.keys[key])}</button>`)).join('')}<p class="key-conflict" id="key-conflict" role="status"></p>${slider('sensitivity', '시점 감도', 25, 200)}${choice(
      'crosshair',
      '조준점 색상',
      [
        ['#f4f2e8', '흰색'],
        ['#88d3cb', '청록'],
        ['#e6bf7a', '노랑'],
      ],
    )}<div class="settings-note"><div class="row" style="flex-wrap:wrap;gap:14px"><span><kbd>W A S D</kbd> 이동</span><span><kbd>Space</kbd> 점프</span><span><kbd>Ctrl</kbd> 앉기</span><span><kbd>마우스 왼쪽</kbd> 공격</span><span><kbd>T / Enter</kbd> 채팅</span><span><kbd>Esc</kbd> 메뉴</span></div><p style="margin-top:14px">스킬은 단축키로 바로 사용합니다. 휠은 스킬 선택에 사용하지 않습니다.</p></div>`;
  return `${modalHead('설정')}<div class="settings-layout"><nav class="settings-tabs" aria-label="설정 분류">${[
    ['system', '시스템'],
    ['sound', '사운드'],
    ['controls', '조작'],
  ]
    .map(
      ([k, n]) =>
        `<button data-settings-tab="${k}" class="${settingsTab === k ? 'active' : ''}" aria-pressed="${settingsTab === k}">${n}</button>`,
    )
    .join(
      '',
    )}</nav><section class="settings-content">${body}</section></div><div class="modal-footer"><p>적용한 설정은 이 브라우저에 저장됩니다.</p><div class="button-row"><button id="reset-settings">기본값</button><button data-close>취소</button><button class="primary" id="save-settings">적용</button></div></div>`;
}
function statPanel() {
  return `${modalHead('스탯 설정')}<div class="stats-layout"><section><div class="preset-tabs">${state.presets.map((p, i) => `<button data-preset="${i}" class="${i === draftPreset ? 'active' : ''}">프리셋 ${i + 1}</button>`).join('')}</div><div class="stat-budget"><span>남은 포인트</span><strong id="stat-remaining"></strong></div>${kinds.map((k, i) => `<div class="stat-row"><label for="stat-${k}"><span>${kindNames[i]} <small>${k}</small></span><output id="value-${k}">${statDraft[i]}</output></label><input id="stat-${k}" aria-label="${kindNames[i]} 투자 포인트" data-stat="${i}" type="range" min="0" max="30" value="${statDraft[i]}"></div>`).join('')}<button id="reset-stats" class="text-button">투자 초기화</button></section><aside class="identity-detail" id="stat-identity"></aside></div><div class="modal-footer"><p>투자 30포인트 · 각 기본 능력치 +5</p><div class="button-row"><button data-close>취소</button><button class="primary" id="save-stats">적용</button></div></div>`;
}
function skillSummary(job) {
  return `<div class="skill-summary">${jobs[job].slots.map((id, i) => `<div><img src="assets/${skills[id].icon}.png" alt=""><span><kbd>${keyLabel(state.settings.keys['skill' + (i + 1)])}</kbd> ${skills[id].name}</span><small>${skills[id].cooldown}초</small></div>`).join('')}</div>`;
}
function updateStatPanel() {
  const job = identity(statDraft),
    j = jobs[job],
    sum = statDraft.reduce((a, b) => a + b, 0);
  $('#stat-remaining').textContent = 30 - sum;
  $('#save-stats').disabled = sum !== 30;
  kinds.forEach((k, i) => {
    $('#value-' + k).textContent = statDraft[i];
    $('#stat-' + k).value = statDraft[i];
  });
  $('#stat-identity').innerHTML =
    `<img src="assets/${j.icon}.png" alt="${j.title}"><h3>${j.title}</h3><p>${j.description}</p>${skillSummary(job)}`;
}
function discardSettings() {
  if (settingsDraft) {
    settingsDraft = null;
    listeningKey = null;
    applySettings(state.settings);
  }
}
function openModal(kind) {
  if (settingsDraft && kind !== 'settings') discardSettings();
  returnFocus = document.activeElement;
  modal.classList.toggle(
    'narrow',
    ['create', 'signup', 'pause', 'leave', 'death', 'connection', 'disconnected'].includes(kind),
  );
  let body = '';
  if (kind === 'settings') {
    settingsDraft ??= clone(state.settings);
    body = settingsPanel();
  }
  if (kind === 'stats') {
    draftPreset = state.preset;
    statDraft = [...state.presets[draftPreset]];
    body = statPanel();
  }
  if (kind === 'character') {
    const job = identity(state.presets[state.preset]),
      j = jobs[job];
    body = `${modalHead('캐릭터', 'JW')}<div class="stats-layout"><div class="identity-detail"><img src="assets/${j.icon}.png" alt=""><h3>${j.title}</h3><p>${j.description}</p>${skillSummary(job)}</div><section><h3>능력치</h3><div class="stat-values">${kinds.map((k, i) => `<div><small>${kindNames[i]}</small><strong>${state.presets[state.preset][i] + 5}</strong></div>`).join('')}</div><div class="divider"></div>${j.slots.map((id) => `<p style="margin-bottom:15px;font-size:14px;line-height:1.7"><strong>${skills[id].name}</strong> · ${skills[id].cooldown}초<br><span class="muted">${skills[id].description}</span></p>`).join('')}</section></div><div class="modal-footer"><small>프리셋 ${state.preset + 1}</small><button class="primary" data-modal="stats">스탯 설정</button></div>`;
  }
  if (kind === 'create')
    body = `${modalHead('방 만들기')}<form id="create-form"><label class="field">방 이름<input name="roomName" required maxlength="32" placeholder="방 이름"></label><label class="field">전장<select name="map"><option value="arena">연구 구역</option><option value="foundry">네온 옥상</option></select></label><p class="muted">개인전 · 최대 8명</p><div class="modal-footer"><button type="button" data-close>취소</button><button type="submit" class="primary">만들기</button></div></form>`;
  if (kind === 'signup')
    body = `${modalHead('회원가입')}<form id="signup-form"><label class="field">아이디<input autocomplete="off" placeholder="아이디" maxlength="32"></label><label class="field">비밀번호<input type="password" autocomplete="off" placeholder="비밀번호"></label><label class="field">비밀번호 확인<input type="password" autocomplete="off" placeholder="비밀번호 확인"></label><p class="settings-note">실제 계정 정보는 입력하지 마세요. 이 시안은 계정을 만들지 않습니다.</p><div class="modal-footer"><button type="button" data-close>취소</button><button class="primary">가입</button></div></form>`;
  if (kind === 'pause')
    body = `${modalHead('메뉴')}<div class="stack"><button class="primary" data-close>돌아가기</button><button data-modal="settings">설정</button><button data-modal="character">캐릭터</button><button data-modal="leave">방 나가기</button></div><p class="settings-note">메뉴를 열어도 온라인 경기는 계속됩니다.</p>`;
  if (kind === 'leave')
    body = `${modalHead('방을 나갈까요?')}<p class="settings-note">${state.host === 'JW' ? '방장이 나가면 방이 종료됩니다.' : '로비로 돌아갑니다.'}</p><div class="modal-footer"><button data-close>취소</button><button id="confirm-leave" class="primary">나가기</button></div>`;
  if (kind === 'death') {
    cancelCast();
    if (!state.dead) deathUntil = Date.now() + 5000;
    state.dead = true;
    body = `${modalHead('사망', '처치자 · LUMEN', false)}<p class="muted" style="text-align:center">부활까지</p><div class="large-counter" id="respawn-counter">5</div><div class="button-row"><button data-modal="stats">스탯 설정</button><button id="respawn" class="primary" disabled>부활</button></div>`;
  }
  if (kind === 'connection')
    body = `${modalHead('방에 입장하는 중')}<div class="status-steps"><span>방 정보 확인</span><span>호스트에 연결 중…</span><span>플레이어 준비</span></div><div class="button-row"><button data-close>취소</button><button data-modal="disconnected">연결 실패 보기</button></div>`;
  if (kind === 'disconnected') {
    cancelCast();
    state.roster = [];
    world?.setPlayers([]);
    $('#game-header').innerHTML = header();
    body = `${modalHead('연결이 종료되었습니다')}<p class="error-copy">호스트가 게임을 종료했거나 연결이 끊어졌습니다.</p><div class="modal-footer"><button class="primary" id="return-rooms">방 목록으로</button></div>`;
  }
  if (kind === 'guide')
    body = `${modalHead('시안 반영 범위')}<div class="guide-copy"><h3>3D 공간</h3><p>로비·대기실·전장 2종을 실제 3D 메시로 구성했습니다. 금속·콘크리트 텍스처는 바닥 UV에 반복 적용하고 하늘은 파노라마로 둘렀습니다. 대기실에는 참가자 수만큼 캐릭터가 배치됩니다.</p><h3>게임 동작</h3><p>직업별 1~2개 스킬을 Q/E로 바로 사용합니다. 슬롯별 대기시간·프리셋 변경·무기 전환, 설정 적용·취소·저장, 입·퇴장 표시를 조작할 수 있습니다.</p><h3>Unity 반영 전</h3><p>캐릭터는 프로젝트 모델의 대기 자세를 사용한 정적 메시입니다. 이동·스킬 이펙트는 확인용이며 피해·충돌·네트워크 판정은 없습니다. 전투 카메라는 몸 일부가 보이는 현재 Unity의 근접 시점을 기준으로 하고, 맵 둘러보기는 외부 시점을 사용합니다. 실제 캐릭터 크기·무기·애니메이션까지 일치하는 것은 아닙니다. 현재 GameObject 맵은 승인 후 구현합니다. 클릭/터치 공격은 사각형 발광 잔상 미리보기입니다. 점프·앉기·피해·명중의 물리 판정은 제공하지 않습니다.</p><p>전장 두 가지는 디자인 후보입니다. 8인 성능과 Unity에서의 재질·조명·충돌·스폰은 별도 검증합니다. 결과 수치는 예시이며 BGM은 기존 로비 음악, 스킬 효과음은 임시 확인음이며 UI 클릭음은 짧은 전자 펄스입니다.</p><h3>직접 확인하기</h3><p>대기실 아래에서 인원을 늘리거나 줄여 보세요. 방장으로 보기를 선택하면 맵 선택과 경기 시작이 활성화됩니다. 맵 둘러보기에서 바닥과 하늘을 켜고 끄며 실제 적용을 확인할 수 있습니다.</p><a href="IMPLEMENTATION.md" target="_blank" rel="noopener">Unity 연결 지점·완료 기준</a></div>`;
  $('#modal-content').innerHTML = body;
  modal.dataset.kind = kind;
  if (!modal.open) modal.showModal();
  world?.setInputEnabled(false);
  if (kind === 'stats') updateStatPanel();
  listeningKey = null;
}
function refreshRoster(message) {
  state.roster = roster();
  if (message) state.chat.push({ name: '', text: message });
  state.chat = state.chat.slice(-30);
  if (state.screen === 'waiting') {
    screen.innerHTML = waiting();
    $('#game-header').innerHTML = header();
    world?.setPlayers(roster());
    renderReview();
  }
}
function flash(text) {
  const el = $('#skill-flash');
  if (!el) return;
  el.textContent = text;
  clearTimeout(flashTimer);
  flashTimer = setTimeout(() => {
    if (el.isConnected) el.textContent = '';
  }, 1600);
}
function basicAttack() {
  if (state.screen !== 'battle' || modal.open || state.dead || state.casting || reviewOpen) return;
  if ($('#battle-chat') && !$('#battle-chat').hidden) return;
  const focused = document.activeElement;
  if (focused?.matches('input,textarea,select') || focused?.isContentEditable) return;
  if (!world?.attack(state.weapon)) return;
  audio.tone('sfx');
  const button = $('#basic-attack');
  if (button) {
    button.dataset.swing = String(Number(button.dataset.swing || 0) + 1);
  }
}
$('#world-canvas').addEventListener('preview-attack', basicAttack);
function castSkill(slot) {
  if (state.screen !== 'battle' || modal.open || state.casting || state.dead) return;
  const id = jobs[state.job].slots[slot];
  if (!id) return;
  const skill = skills[id],
    now = performance.now();
  if ((state.cooldowns[id] || 0) > now) {
    flash('아직 사용할 수 없습니다');
    return;
  }
  state.cooldowns[id] = now + skill.cooldown * 1000;
  state.casting = id;
  audio.tone('sfx');
  flash(skill.name);
  world?.effect(id === 'str' ? 0xd89577 : 0xd9d3a0);
  castTimer = setTimeout(() => {
    if (state.screen !== 'battle' || state.casting !== id) return;
    state.casting = null;
    if (id === 'dash' || id === 'roll') world?.move(lastDirection[0] * 3.5, lastDirection[1] * 3.5);
    if (id === 'weapon') {
      state.weapon = state.weapon === '검' ? '활' : '검';
      $('#weapon-name').textContent = state.weapon;
      flash(state.weapon + '으로 전환');
    }
    if (id === 'preset') {
      state.preset = (state.preset + 1) % state.presets.length;
      state.job = identity(state.presets[state.preset]);
      $('#skill-bar').innerHTML = skillSlots();
      $('.health-title strong').textContent = 'JW · ' + jobs[state.job].title;
      $('.health small').textContent = '프리셋 ' + (state.preset + 1);
      world?.setPlayers(roster());
      renderReview();
      flash('프리셋 ' + (state.preset + 1));
    }
  }, skill.cast * 1000);
  syncCooldowns();
}
function syncCooldowns() {
  const now = performance.now();
  $$('[data-skill-id]').forEach((button) => {
    const id = button.dataset.skillId,
      remaining = Math.max(0, (state.cooldowns[id] || 0) - now);
    $('.cd', button).textContent =
      state.casting === id ? '시전' : remaining > 0 ? (remaining / 1000).toFixed(1) : '';
    button.style.setProperty('--cooldown', (remaining / (skills[id].cooldown * 1000)) * 100 + '%');
    button.setAttribute('aria-disabled', String(remaining > 0 || !!state.casting));
  });
  if (modal.open && modal.dataset.kind === 'death') {
    const seconds = Math.max(0, Math.ceil((deathUntil - Date.now()) / 1000));
    $('#respawn-counter').textContent = seconds;
    $('#respawn').disabled = seconds > 0;
  }
}
setInterval(syncCooldowns, 100);
document.addEventListener('click', async (e) => {
  const b = e.target.closest('button');
  if (!b || b.disabled) return;
  if (!b.dataset.tone && b.id !== 'basic-attack') audio.tone('ui');
  if (b.id === 'basic-attack') {
    basicAttack();
    return;
  }
  if (b.dataset.go) {
    if (['lobby', 'login'].includes(b.dataset.go) && !audio.started) audio.start();
    render(b.dataset.go);
    return;
  }
  if (b.dataset.modal) {
    openModal(b.dataset.modal);
    return;
  }
  if (b.hasAttribute('data-close')) {
    if (state.dead && modal.dataset.kind === 'death') return;
    modal.close();
    return;
  }
  if (b.dataset.settingsTab) {
    listeningKey = null;
    settingsTab = b.dataset.settingsTab;
    $('#modal-content').innerHTML = settingsPanel();
    return;
  }
  if (b.dataset.rebind) {
    listeningKey = b.dataset.rebind;
    $('#modal-content').innerHTML = settingsPanel();
    return;
  }
  if (b.dataset.room !== undefined) {
    state.selected = Number(b.dataset.room);
    screen.innerHTML = roomList();
    return;
  }
  if (b.dataset.map) {
    state.viewMap = b.dataset.map;
    render('maps');
    return;
  }
  if (b.dataset.camera) {
    world?.[b.dataset.camera === 'reset' ? 'resetCamera' : b.dataset.camera]();
    return;
  }
  if (b.dataset.asset) {
    const names = { 'alloy-floor': '금속 패널', 'rooftop-floor': '옥상 콘크리트', 'night-city-sky': '하늘' };
    $('#modal-content').innerHTML =
      `${modalHead(names[b.dataset.asset])}<img class="asset-preview" src="assets/${b.dataset.asset}.png" alt="${names[b.dataset.asset]}"><div class="modal-footer"><a href="assets/${b.dataset.asset}.png" download>원본 저장</a><button data-close>닫기</button></div>`;
    modal.classList.remove('narrow');
    modal.dataset.kind = 'asset';
    modal.showModal();
    world?.setInputEnabled(false);
    return;
  }
  if (b.dataset.preset !== undefined) {
    draftPreset = Number(b.dataset.preset);
    statDraft = [...state.presets[draftPreset]];
    $('#modal-content').innerHTML = statPanel();
    updateStatPanel();
    return;
  }
  if (b.dataset.skill !== undefined) {
    castSkill(Number(b.dataset.skill));
    return;
  }
  if (b.dataset.move) {
    lastDirection = b.dataset.move.split(',').map(Number);
    if (!state.dead && !modal.open) world?.move(...lastDirection);
    return;
  }
  if (b.dataset.tone) {
    audio.tone(b.dataset.tone);
    return;
  }
  switch (b.id) {
    case 'review-toggle':
      reviewOpen = !reviewOpen;
      renderReview();
      break;
    case 'join-room': {
      const r = rooms[state.selected];
      if (r.count >= 8) return;
      state.roomName = r.name;
      state.host = r.host;
      state.map = r.map;
      state.roster = [
        me(),
        { name: r.host, job: '전략가' },
        ...samplePeople.filter((p) => !p.me && p.name !== r.host),
      ].slice(0, r.count + 1);
      state.chat = [{ name: '', text: '대기실에 입장했습니다.' }];
      render('waiting');
      break;
    }
    case 'refresh-rooms':
      toast('목록을 새로 불러왔습니다');
      break;
    case 'add-player': {
      if (state.roster.length >= 8) return;
      const next = samplePeople.find((p) => !state.roster.some((x) => x.name === p.name));
      if (next) {
        state.roster.push({ ...next, host: next.name === state.host });
        refreshRoster(next.name + ' 님이 입장했습니다.');
      }
      break;
    }
    case 'remove-player': {
      const index = state.roster.findLastIndex((p) => !p.me && p.name !== state.host);
      if (index >= 0) {
        const [p] = state.roster.splice(index, 1);
        refreshRoster(p.name + ' 님이 퇴장했습니다.');
      } else if (state.roster.length > 1) {
        openModal('disconnected');
      }
      break;
    }
    case 'host-switch': {
      if (state.host === 'JW') {
        let other = state.roster.find((p) => !p.me);
        if (!other) {
          other = { ...samplePeople[1] };
          state.roster.push(other);
        }
        state.host = other.name;
      } else state.host = 'JW';
      refreshRoster();
      break;
    }
    case 'start-match':
      if (state.host === 'JW') {
        state.job = identity(state.presets[state.preset]);
        render('battle');
      }
      break;
    case 'toggle-textures':
      world.flags.textures = !world.flags.textures;
      render('maps');
      break;
    case 'toggle-sky':
      world.flags.sky = !world.flags.sky;
      render('maps');
      break;
    case 'reset-stats':
      statDraft = [0, 0, 0, 0];
      updateStatPanel();
      break;
    case 'save-stats':
      if (statDraft.reduce((a, b) => a + b, 0) !== 30) return;
      state.presets[draftPreset] = [...statDraft];
      state.preset = draftPreset;
      state.job = identity(statDraft);
      render();
      toast('프리셋 ' + (state.preset + 1) + ' 적용');
      break;
    case 'reset-settings':
      settingsDraft = clone(defaults);
      listeningKey = null;
      applySettings(settingsDraft);
      $('#modal-content').innerHTML = settingsPanel();
      break;
    case 'save-settings': {
      state.settings = clone(settingsDraft);
      settingsDraft = null;
      listeningKey = null;
      try {
        localStorage.setItem('battle-pvp-preview-settings-v2', JSON.stringify(state.settings));
        toast('설정을 적용했습니다');
      } catch {
        toast('설정을 적용했습니다. 브라우저 저장은 사용할 수 없습니다.');
      }
      modal.close();
      applySettings(state.settings);
      if (state.screen === 'battle') $('#skill-bar').innerHTML = skillSlots();
      break;
    }
    case 'test-music':
      if (audio.started) {
        audio.stop();
        b.textContent = '음악 듣기';
      } else {
        const ok = await audio.start();
        b.textContent = ok ? '음악 정지' : '음악 듣기';
        if (!ok) toast('소리를 재생하지 못했습니다. 다시 눌러 주세요.');
      }
      break;
    case 'fullscreen':
      try {
        if (document.fullscreenElement) await document.exitFullscreen();
        else if (game.requestFullscreen) await game.requestFullscreen();
        else toast('이 브라우저에서는 전체화면 전환을 지원하지 않습니다.');
      } catch {
        toast('전체화면으로 전환하지 못했습니다.');
      }
      break;
    case 'respawn':
      if (Date.now() >= deathUntil) {
        state.dead = false;
        modal.close();
        world?.effect();
        toast('부활했습니다');
      }
      break;
    case 'confirm-leave':
      state.roster = [me()];
      state.host = 'JW';
      state.chat = [];
      render('lobby');
      break;
    case 'return-rooms':
      state.roster = clone(samplePeople);
      state.host = 'LUMEN';
      render('rooms');
      break;
    case 'host-disconnect':
      openModal('disconnected');
      break;
  }
});
document.addEventListener('input', (e) => {
  const t = e.target;
  if (t.dataset.setting) {
    const key = t.dataset.setting;
    settingsDraft[key] =
      t.type === 'checkbox'
        ? t.checked
        : t.tagName === 'SELECT'
          ? ['fps'].includes(key)
            ? Number(t.value)
            : t.value
          : Number(t.value);
    const output = $(`[data-output="${key}"]`);
    if (output) output.textContent = settingsDraft[key] + '%';
    applySettings(settingsDraft);
  }
  if (t.dataset.stat !== undefined) {
    const i = Number(t.dataset.stat),
      other = statDraft.reduce((a, b, j) => a + (j === i ? 0 : b), 0);
    statDraft[i] = Math.min(30 - other, Math.max(0, Number(t.value)));
    updateStatPanel();
  }
});
document.addEventListener('change', (e) => {
  if (e.target.id === 'room-map' && state.host === 'JW') state.map = e.target.value;
  if (e.target.id === 'preview-job') {
    cancelCast();
    state.job = e.target.value;
    state.weapon = '검';
    screen.innerHTML = battle();
    world?.setPlayers(roster());
    syncCooldowns();
  }
});
document.addEventListener('submit', (e) => {
  e.preventDefault();
  const form = e.target;
  if (form.id === 'login-form') {
    form.reset();
    audio.start();
    render('lobby');
  }
  if (form.id === 'signup-form') {
    form.reset();
    modal.close();
    toast('회원가입 화면을 확인했습니다');
  }
  if (form.id === 'create-form') {
    const name = form.elements.roomName.value.trim();
    if (!name) return;
    state.roomName = name;
    state.map = form.elements.map.value;
    state.host = 'JW';
    state.roster = [me()];
    state.chat = [{ name: '', text: '방을 만들었습니다.' }];
    render('waiting');
  }
  if (form.id === 'chat-form') {
    const input = form.elements.message,
      text = input.value.trim();
    if (!text) return;
    state.chat.push({ name: 'JW', text });
    state.chat = state.chat.slice(-30);
    $('#chat-lines').innerHTML = state.chat
      .slice(-5)
      .map(
        (m) =>
          `<p class="${m.name ? '' : 'system'}">${m.name ? '<b>' + esc(m.name) + '</b>' : ''}${esc(m.text)}</p>`,
      )
      .join('');
    input.value = '';
    $('#chat-lines').scrollTop = $('#chat-lines').scrollHeight;
  }
});
document.addEventListener('keydown', (e) => {
  if (listeningKey && settingsDraft) {
    e.preventDefault();
    e.stopPropagation();
    if (e.code === 'Escape') {
      listeningKey = null;
      $('#modal-content').innerHTML = settingsPanel();
      return;
    }
    if (!validKey(e.code)) {
      $('#key-conflict').textContent =
        '이동·채팅·메뉴 키는 사용할 수 없습니다. 다른 문자 또는 숫자 키를 눌러 주세요.';
      return;
    }
    const other = listeningKey === 'skill1' ? 'skill2' : 'skill1';
    if (settingsDraft.keys[other] === e.code) {
      $('#key-conflict').textContent = '다른 스킬이 사용 중인 키입니다.';
      return;
    }
    settingsDraft.keys[listeningKey] = e.code;
    listeningKey = null;
    $('#modal-content').innerHTML = settingsPanel();
    return;
  }
  if (e.code === 'Escape') {
    if (!modal.open && e.target.matches('input,textarea')) {
      e.preventDefault();
      e.target.blur();
      if ($('#battle-chat')) $('#battle-chat').hidden = true;
      return;
    }
    if (modal.open) return;
    if (reviewOpen) {
      reviewOpen = false;
      renderReview();
      return;
    }
    if (['battle', 'waiting'].includes(state.screen)) {
      e.preventDefault();
      openModal('pause');
    }
    return;
  }
  if (modal.open || e.target.matches('input,textarea,select') || e.target.isContentEditable) return;
  if (state.screen === 'title' && (e.code === 'Enter' || e.code === 'Space')) {
    e.preventDefault();
    audio.start();
    render('login');
    return;
  }
  if (state.screen === 'waiting' && (e.code === 'Enter' || e.code === 'KeyT')) {
    e.preventDefault();
    $('#chat-form input')?.focus();
    return;
  }
  if (state.screen !== 'battle' || state.dead) return;
  if (e.code === 'Enter' || e.code === 'KeyT') {
    e.preventDefault();
    $('#battle-chat').hidden = false;
    $('#battle-chat input').focus();
    return;
  }
  if (e.code === state.settings.keys.skill1 || e.code === state.settings.keys.skill2) {
    e.preventDefault();
    if (!e.repeat) castSkill(e.code === state.settings.keys.skill1 ? 0 : 1);
    return;
  }
  const dir = { KeyW: [0, -1], KeyA: [-1, 0], KeyS: [0, 1], KeyD: [1, 0] }[e.code];
  if (dir) {
    e.preventDefault();
    lastDirection = dir;
    world?.move(...dir);
  }
});
modal.addEventListener('cancel', (e) => {
  if (state.dead && modal.dataset.kind === 'death') e.preventDefault();
});
modal.addEventListener('close', () => {
  discardSettings();
  if (modal.dataset.kind === 'disconnected' && state.screen !== 'rooms') {
    state.roster = clone(samplePeople);
    state.host = 'LUMEN';
    render('rooms');
    return;
  }
  if (state.dead && state.screen === 'battle') {
    openModal('death');
    return;
  }
  world?.setInputEnabled(true);
  if (returnFocus?.isConnected) returnFocus.focus({ preventScroll: true });
});
window.addEventListener('hashchange', () => render(location.hash.slice(1)));
render(location.hash.slice(1) || 'title');
