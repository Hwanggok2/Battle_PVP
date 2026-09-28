import * as THREE from 'three';
import { OrbitControls } from './vendor/OrbitControls.js';
import { GLTFLoader } from './vendor/loaders/GLTFLoader.js';
import { BlockSlashEffect } from './combat-effects.js';
import { addMapNeon } from './map-neon.js';
const palette = [0xa7e6f0, 0x75cbbd, 0xb5a3e4, 0xe39fa8, 0x95c8a8, 0xdba7d5, 0x91b7e6, 0xc4cbd5];
export const mapInfo = {
  lobby: {
    name: '작전실',
    kind: '로비',
    size: '28 × 24 m',
    text: '도시가 내려다보이는 작전실. 장비를 점검하고 다음 경기를 준비합니다.',
  },
  waiting: {
    name: '출격 격납고',
    kind: '대기실',
    size: '24 × 20 m',
    text: '8명이 함께 모이는 출격 격납고. 장비 보관함과 출입문 사이 중앙 공간에서 경기를 기다립니다.',
  },
  arena: {
    name: '연구 구역',
    kind: 'Battle A',
    size: '36 × 32 m',
    text: '격리 장비 네 개를 중심으로 교전하는 연구 시설. 양옆 정비 통로로 우회할 수 있습니다.',
  },
  foundry: {
    name: '네온 옥상',
    kind: 'Battle B',
    size: '36 × 32 m',
    text: '야간 도시의 고층 옥상. 환기 장치와 교차 엄폐벽 사이를 오가며 전투합니다.',
  },
};
export class GameWorld {
  constructor(canvas, labels) {
    this.canvas = canvas;
    this.labels = labels;
    this.people = [];
    this.effects = [];
    this.map = 'lobby';
    this.ready = false;
    this.flags = { textures: true, sky: true };
    this.frameLimit = 60;
    this.lastFrame = 0;
    this.renderer = new THREE.WebGLRenderer({ canvas, antialias: true, powerPreference: 'high-performance' });
    this.renderer.setPixelRatio(Math.min(devicePixelRatio, 1.5));
    this.renderer.outputColorSpace = THREE.SRGBColorSpace;
    this.renderer.toneMapping = THREE.ACESFilmicToneMapping;
    this.renderer.toneMappingExposure = 1.35;
    this.renderer.shadowMap.enabled = true;
    this.renderer.shadowMap.type = THREE.PCFSoftShadowMap;
    this.scene = new THREE.Scene();
    this.attackEffect = new BlockSlashEffect(this.scene);
    this.scene.background = new THREE.Color(0x091123);
    this.scene.fog = new THREE.Fog(0x101c35, 65, 155);
    this.camera = new THREE.PerspectiveCamera(43, 1, 0.1, 250);
    this.controls = new OrbitControls(this.camera, canvas);
    this.controls.target.set(0, 1, 0);
    this.controls.enableDamping = true;
    this.controls.enablePan = false;
    this.controls.minDistance = 8;
    this.controls.maxDistance = 85;
    this.controls.minPolarAngle = 0.2;
    this.controls.maxPolarAngle = Math.PI * 0.47;
    this.controls.rotateSpeed = 0.55;
    this.inputEnabled = true;
    this.lookYaw = 0;
    this.lookPitch = 0;
    this.lookSensitivity = 1;
    canvas.addEventListener('pointerdown', (event) => {
      if (this.mode !== 'battle' || !this.inputEnabled || event.button !== 0 || this.lookPointer) return;
      this.lookPointer = { id: event.pointerId, x: event.clientX, y: event.clientY, travel: 0 };
      canvas.setPointerCapture(event.pointerId);
    });
    canvas.addEventListener('pointermove', (event) => {
      const p = this.lookPointer;
      if (!p || p.id !== event.pointerId || !this.inputEnabled) return;
      p.travel += Math.hypot(event.clientX - p.x, event.clientY - p.y);
      this.lookYaw -= (event.clientX - p.x) * 0.003 * this.lookSensitivity;
      this.lookPitch = THREE.MathUtils.clamp(
        this.lookPitch - (event.clientY - p.y) * 0.003 * this.lookSensitivity,
        -Math.PI / 3,
        Math.PI / 3,
      );
      p.x = event.clientX;
      p.y = event.clientY;
      this.syncPlayerCamera();
    });
    canvas.addEventListener('pointerup', (event) => {
      if (this.lookPointer?.id === event.pointerId && this.lookPointer.travel < 8 && this.inputEnabled)
        canvas.dispatchEvent(new CustomEvent('preview-attack'));
      this.lookPointer = null;
    });
    for (const event of ['pointercancel', 'lostpointercapture'])
      canvas.addEventListener(event, () => {
        this.lookPointer = null;
      });
    this.scene.add(new THREE.HemisphereLight(0xb6c8ee, 0x192137, 1.9));
    this.sun = new THREE.DirectionalLight(0xc0d2f0, 2.4);
    this.sun.position.set(-18, 32, 16);
    this.sun.castShadow = true;
    this.sun.shadow.mapSize.set(1024, 1024);
    Object.assign(this.sun.shadow.camera, { left: -30, right: 30, top: 30, bottom: -30, near: 1, far: 90 });
    this.sun.shadow.bias = -0.001;
    this.sun.shadow.normalBias = 0.04;
    this.scene.add(this.sun);
    this.land = new THREE.Group();
    this.avatars = new THREE.Group();
    this.scene.add(this.land, this.avatars);
    this.resizeObserver = new ResizeObserver(() => this.resize());
    this.resizeObserver.observe(canvas.parentElement);
    this.resize();
    this.renderer.setAnimationLoop(this.animate.bind(this));
  }
  async load() {
    const loader = new THREE.TextureLoader();
    const [stone, metal, sky, model] = await Promise.all([
      loader.loadAsync('assets/alloy-floor.png'),
      loader.loadAsync('assets/rooftop-floor.png'),
      loader.loadAsync('assets/night-city-sky.png'),
      new GLTFLoader().loadAsync('assets/models/player.glb'),
    ]);
    for (const tex of [stone, metal]) {
      tex.colorSpace = THREE.SRGBColorSpace;
      tex.wrapS = tex.wrapT = THREE.RepeatWrapping;
      tex.anisotropy = Math.min(4, this.renderer.capabilities.getMaxAnisotropy());
    }
    sky.colorSpace = THREE.SRGBColorSpace;
    sky.mapping = THREE.EquirectangularReflectionMapping;
    this.skyTexture = sky;
    this.scene.background = sky;
    this.stone = stone;
    this.metal = metal;
    this.character = model.scene;
    const bounds = new THREE.Box3().setFromObject(this.character),
      height = bounds.max.y - bounds.min.y;
    // The exported project character is about 1.75 m tall; avoid inflating the close camera silhouette.
    this.character.scale.setScalar(1.75 / height);
    this.character.position.y = (-bounds.min.y * 1.75) / height;
    this.ready = true;
    this.setMap(this.map, this.mode);
    return this;
  }
  material(color, extra = {}) {
    return new THREE.MeshStandardMaterial({ color, roughness: 0.9, ...extra });
  }
  mesh(geometry, material, x = 0, y = 0, z = 0, parent = this.land) {
    const m = new THREE.Mesh(geometry, material);
    m.position.set(x, y, z);
    m.castShadow = true;
    m.receiveShadow = true;
    parent.add(m);
    return m;
  }
  box(w, h, d, x, y, z, color, parent = this.land) {
    return this.mesh(
      new THREE.BoxGeometry(w, h, d),
      typeof color === 'number' ? this.material(color) : color,
      x,
      y,
      z,
      parent,
    );
  }
  ring(radius, color, x = 0, z = 0, y = 0.045) {
    const m = this.mesh(
      new THREE.RingGeometry(radius - 0.07, radius + 0.07, 64),
      new THREE.MeshBasicMaterial({ color, side: THREE.DoubleSide }),
      x,
      y,
      z,
    );
    m.rotation.x = -Math.PI / 2;
    return m;
  }
  clearLand() {
    const geometries = new Set(),
      materials = new Set();
    this.land.traverse((m) => {
      if (m.isMesh) {
        geometries.add(m.geometry);
        [m.material].flat().forEach((v) => materials.add(v));
      }
    });
    this.land.clear();
    for (const g of geometries) g.dispose();
    for (const m of materials) {
      if (m.map) m.map.dispose();
      m.dispose();
    }
  }
  glow(color, intensity = 0.65) {
    return this.material(color, {
      emissive: color,
      emissiveIntensity: intensity,
      roughness: 0.45,
      toneMapped: false,
    });
  }
  floor(w, d, rooftop = false) {
    let tex = null;
    if (this.flags.textures) {
      tex = (rooftop ? this.metal : this.stone).clone();
      tex.needsUpdate = true;
      tex.repeat.set(w / 5, d / 5);
    }
    this.box(w, 2, d, 0, -1.1, 0, 0x18253a);
    const ground = this.mesh(
      new THREE.PlaneGeometry(w, d),
      this.material(0xb8c9df, { map: tex, metalness: rooftop ? 0.08 : 0.32, roughness: 0.75 }),
      0,
      -0.025,
      0,
    );
    ground.name = rooftop ? 'Floor_Concrete' : 'Floor_Alloy';
    ground.rotation.x = -Math.PI / 2;
    for (const x of [-w / 2, w / 2]) {
      this.box(0.4, 0.7, d, x, 0.2, 0, 0x293b52);
      this.box(0.08, 0.04, d - 1, x - Math.sign(x) * 0.35, 0.015, 0, this.glow(0x53cfd8));
    }
    for (const z of [-d / 2, d / 2]) this.box(w, 0.7, 0.4, 0, 0.2, z, 0x293b52);
    for (const x of [-w / 2 + 1, w / 2 - 1]) this.box(1.1, 9, 1.1, x, -4.5, -d / 2 + 1, 0x111c2e);
  }
  strip(w, h, d, x, y, z, color = 0x54dbe2) {
    const m = this.box(w, h, d, x, y, z, this.glow(color));
    m.castShadow = false;
    return m;
  }
  bulkhead(x, z, h = 6) {
    this.box(0.8, h, 1.2, x, h / 2, z, 0x293d55);
    this.box(1.2, 0.35, 1.5, x, 0.2, z, 0x46566d);
    this.strip(0.12, h * 0.6, 0.03, x, h * 0.52, z + 0.62);
  }
  door(z, width = 8) {
    for (const x of [-width / 2, width / 2]) this.bulkhead(x, z, 6);
    this.box(width + 1, 0.7, 1.2, 0, 6, z, 0x40516b);
    this.box(width - 0.8, 5.5, 0.7, 0, 2.75, z, 0x16263a);
    for (const x of [-width / 4, width / 4]) {
      this.box(width / 2 - 0.5, 4.8, 0.3, x, 2.65, z + 0.5, 0x34475e);
      for (const y of [1.5, 2, 2.5, 3, 3.5]) this.box(width / 2 - 1, 0.07, 0.06, x, y, z + 0.67, 0x192539);
    }
    this.strip(0.09, 4.9, 0.1, 0, 2.65, z + 0.72);
    this.strip(width - 1, 0.14, 0.1, 0, 5.55, z + 0.75);
    for (const x of [-width / 2 + 0.6, width / 2 - 0.6]) this.strip(0.3, 0.75, 0.1, x, 1, z + 0.7, 0xe2ae62);
  }
  equipment(x, z, width = 2.4, height = 3.3) {
    this.box(width, height, 1.3, x, height / 2, z, 0x26384f);
    this.box(width - 0.25, height - 0.35, 0.08, x, height / 2, z + 0.68, 0x111d30);
    for (let i = 0; i < 4; i++) this.box(width - 0.55, 0.13, 0.1, x, 0.65 + i * 0.35, z + 0.76, 0x526277);
    this.strip(width - 0.6, 0.13, 0.1, x, height - 0.4, z + 0.77);
    this.strip(0.14, 0.14, 0.1, x + width / 2 - 0.35, 0.3, z + 0.77, 0xda75c0);
  }
  console(x, z, width = 3) {
    this.box(width, 1, 1.2, x, 0.5, z, 0x293a52);
    const panel = this.box(width - 0.25, 0.9, 0.12, x, 1.35, z - 0.25, 0x122033);
    panel.rotation.x = -0.35;
    for (const row of [0, 1, 2])
      this.strip(
        width - 0.65,
        0.035,
        0.03,
        x,
        1.1 + row * 0.19,
        z - 0.15 - row * 0.06,
        row === 0 ? 0x9ec4e0 : 0x4ca7b5,
      );
  }
  vent(x, z, w = 3.5, d = 2, h = 2.2) {
    this.box(w, h, d, x, h / 2, z, 0x35485f);
    this.box(w + 0.15, 0.18, d + 0.15, x, h, z, 0x53647a);
    for (let i = 0; i < 5; i++) this.box(w - 0.5, 0.07, 0.09, x, 0.45 + i * 0.27, z + d / 2 + 0.04, 0x17283d);
    this.strip(w * 0.6, 0.08, 0.07, x, h - 0.2, z + d / 2 + 0.08);
  }
  rearWall(w, z) {
    this.box(w, 1.7, 0.5, 0, 0.85, z, 0x24364c);
    this.box(w, 1, 0.65, 0, 7, z, 0x26394f);
    this.strip(w - 1, 0.09, 0.12, 0, 6.4, z + 0.4);
    for (const x of [-w / 2, -w / 4, 0, w / 4, w / 2]) this.bulkhead(x, z, 7);
  }
  setMap(key, mode = 'map') {
    this.map = key in mapInfo ? key : 'lobby';
    this.mode = mode;
    this.attackEffect?.clear();
    if (!this.ready) return;
    this.clearLand();
    this.effects = [];
    this.scene.background = this.flags.sky ? this.skyTexture : new THREE.Color(0x0a1224);
    const battle = ['arena', 'foundry'].includes(this.map);
    this.floor(
      battle ? 36 : this.map === 'waiting' ? 24 : 28,
      battle ? 32 : this.map === 'waiting' ? 20 : 24,
      this.map === 'foundry',
    );
    if (this.map === 'lobby') {
      this.rearWall(27, -10.5);
      // Open window bays preserve the city view and the player's silhouette.
      for (const x of [-10, 10]) {
        this.equipment(x, -7);
        this.equipment(x, -4, 2.4, 2.7);
        this.box(0.6, 3.2, 13, x * 1.3, 1.6, -1, 0x21334c);
        this.strip(0.08, 0.08, 12, x * 1.3 - Math.sign(x) * 0.4, 3.3, -1, x < 0 ? 0x53d6df : 0xcc70bd);
      }
      for (const x of [-5, 0, 5]) this.console(x, -7.5, 3.6);
      this.box(7, 0.25, 4, 0, 0.1, -4.8, 0x2b4055);
      this.strip(7, 0.05, 0.06, 0, 0.26, -2.8);
      for (const x of [-3, 3]) this.strip(0.08, 0.035, 10, x, 0.012, 3);
      for (const z of [0, 5]) this.strip(6, 0.035, 0.08, 0, 0.014, z);
      this.ring(1.5, 0x6bd5df, 0, 2);
    } else if (this.map === 'waiting') {
      this.box(24, 7, 0.5, 0, 3.5, -9.6, 0x1a2b41);
      this.door(-9);
      for (const x of [-9, 9]) {
        for (const z of [-6, -3]) this.equipment(x, z, 2.2, 3.3);
        this.bulkhead(x, -8, 7);
        this.box(0.7, 3, 17, x * 1.25, 1.5, 0, 0x21344e);
        this.strip(0.09, 0.07, 16, x * 1.25 - Math.sign(x) * 0.42, 3.04, 0, x < 0 ? 0x55d8df : 0xcd75be);
        this.box(3.5, 0.55, 1, x, 0.3, 6.5, 0x3c4c61);
      }
      this.box(21, 0.55, 0.7, 0, 7, -7.4, 0x33465c);
      this.strip(18, 0.09, 0.4, 0, 6.7, -7.4, 0xb0cfe4);
      for (const x of [-6.7, 6.7]) this.strip(0.07, 0.035, 13, x, 0.014, 0);
      for (const z of [-6.4, 6.4]) this.strip(13.4, 0.035, 0.07, 0, 0.014, z);
      for (let i = 0; i < 5; i++) this.box(0.7, 0.035, 0.2, -1.6 + i * 0.8, 0.015, -7.5, 0xd3ac6a);
    } else if (this.map === 'arena') {
      this.rearWall(35, -15);
      for (const [x, z] of [
        [-6, -5],
        [6, -5],
        [-6, 5],
        [6, 5],
      ]) {
        this.vent(x, z);
        this.box(2.7, 1.2, 1.3, x, 2.85, z, 0x1d324c);
        this.strip(0.12, 0.9, 0.06, x - 1, 2.85, z + 0.69, 0x7de4e2);
      }
      for (const x of [-15, 15]) {
        for (const z of [-10, 0, 10]) this.bulkhead(x, z, 5.5);
        this.box(0.6, 0.8, 27, x + Math.sign(x), 0.4, 0, 0x31445d);
        this.strip(0.07, 0.03, 26, x - Math.sign(x) * 1.3, 0.012, 0);
        this.box(0.3, 0.3, 27, x, 5.5, 0, 0x53627b);
      }
      for (const x of [-9, 9]) this.equipment(x, -13.5, 3, 4);
      for (const z of [-3.4, 3.4]) this.strip(6.8, 0.03, 0.07, 0, 0.014, z);
      for (const x of [-3.4, 3.4]) this.strip(0.07, 0.03, 6.8, x, 0.014, 0);
    } else {
      for (const [x, z, w, d] of [
        [-6, -6, 7, 1.6],
        [6, 6, 7, 1.6],
        [6, -6, 1.6, 7],
        [-6, 6, 1.6, 7],
      ])
        this.vent(x, z, w, d, 2.3);
      for (const x of [-14, 14])
        for (const z of [-12, 12]) {
          this.vent(x, z, 2.5, 2.5, 3);
          this.box(0.12, 3.5, 0.12, x, 4.8, z, 0x697a92);
          this.strip(0.17, 0.22, 0.17, x, 6.5, z, 0xda75b7);
        }
      for (const z of [-15, 15]) {
        this.box(34, 0.9, 0.5, 0, 0.45, z, 0x34475e);
        this.strip(32, 0.08, 0.06, 0, 0.95, z, z < 0 ? 0xd076c3 : 0x5bcddc);
      }
      for (const x of [-16, 16]) this.box(0.45, 0.9, 29, x, 0.45, 0, 0x34475e);
      this.box(5, 0.035, 5, 0, 0.012, 0, 0x273b50);
      for (const x of [-2.5, 2.5]) this.strip(0.06, 0.035, 5, x, 0.035, 0, 0x74aabd);
      // Adjacent building masses sit below the playable roof, with the panorama beyond.
      for (const [x, z, h] of [
        [-29, -25, 24],
        [25, -28, 31],
        [-34, 8, 16],
        [32, 6, 19],
      ]) {
        this.box(9, h, 10, x, h / 2 - 20, z, 0x142039);
        for (let j = 0; j < 3; j++)
          this.strip(6, 0.16, 0.06, x, h - 22 - j * 2.6, z + 5.04, j === 1 ? 0xa562a3 : 0x3e738d);
      }
    }
    addMapNeon(this, this.map);
    this.resetCamera();
    this.setPlayers(this.roster || []);
  }
  resetCamera() {
    this.camera.fov = this.mode === 'battle' ? 60 : 43;
    this.camera.updateProjectionMatrix();
    this.setInputEnabled(this.inputEnabled);
    if (this.mode === 'battle') {
      this.lookYaw = 0;
      this.lookPitch = 0;
      this.syncPlayerCamera();
      return;
    }
    const portrait = this.canvas.clientWidth / this.canvas.clientHeight < 0.9,
      battle = ['arena', 'foundry'].includes(this.map),
      k = portrait ? 1.23 : 1;
    let p;
    if (this.mode === 'title') p = [0, 5, 28];
    else if (battle) p = [28, 30, 37];
    else if (this.map === 'waiting') p = [0, 12, 28];
    else p = [22, 12, 26];
    this.camera.position.set(p[0] * k, p[1] * k, p[2] * k);
    this.controls.target.set(this.mode === 'lobby' ? 2 : 0, 0, 0);
    this.controls.update();
  }
  overhead() {
    if (this.mode === 'battle') return;
    this.camera.position.set(0, 57, 0.1);
    this.controls.target.set(0, 0, 0);
    this.controls.update();
  }
  closeup() {
    if (this.mode === 'battle') return;
    this.camera.position.set(7, 6, 11);
    this.controls.target.set(0, 0.2, 0);
    this.controls.update();
  }
  setPlayers(roster) {
    this.roster = roster;
    if (!this.ready) return;
    const previousPosition =
      this.mode === 'battle' && this.playerMap === this.map && this.playerMode === this.mode
        ? this.people.find((p) => p.me)?.group.position.clone()
        : null;
    for (const p of this.people) {
      p.label.remove();
      p.group.traverse((m) => {
        if (m.isMesh && m.userData.privateMaterial) [m.material].flat().forEach((v) => v.dispose());
      });
      p.ring.geometry.dispose();
      p.ring.material.dispose();
    }
    this.avatars.clear();
    this.people = [];
    const positions =
      this.map === 'waiting'
        ? [
            [-4, 2],
            [-1, 3],
            [2, 3],
            [5, 2],
            [-5, -1],
            [-2, -2],
            [1, -2],
            [4, -1],
          ]
        : this.map === 'lobby'
          ? [[0, 2]]
          : [
              [-10, 9],
              [-9, -10],
              [9, -10],
              [10, 9],
              [-12, 0],
              [12, 0],
              [0, 12],
              [0, -12],
            ];
    roster.forEach((person, i) => {
      const group = new THREE.Group(),
        body = this.character.clone(true),
        color = person.me && this.mode === 'battle' ? 0x506d88 : palette[i % palette.length];
      body.traverse((m) => {
        if (m.isMesh) {
          const original = m.material;
          const materials = [original].flat().map((mat) => {
            const n = mat.clone();
            n.color.setHex(color);
            return n;
          });
          m.material = Array.isArray(original) ? materials : materials[0];
          m.userData.privateMaterial = true;
          m.castShadow = true;
          m.receiveShadow = true;
        }
      });
      group.add(body);
      if (['모노스탯 · 힘', '모노스탯 · 체력'].includes(person.job)) body.scale.multiplyScalar(1.2);
      const [x, z] = positions[i % positions.length];
      group.position.set(x, 0, z);
      if (person.me && previousPosition) group.position.copy(previousPosition);
      group.rotation.y = 0.3 + i * 0.14;
      const ring = new THREE.Mesh(
        new THREE.RingGeometry(0.65, 0.72, 32),
        new THREE.MeshBasicMaterial({ color: person.me ? 0x76ffff : 0x9baed3, side: THREE.DoubleSide }),
      );
      ring.rotation.x = -Math.PI / 2;
      ring.position.y = 0.04;
      group.add(ring);
      this.avatars.add(group);
      const label = document.createElement('div');
      label.className = 'player-label' + (person.me ? ' mine' : '');
      const name = document.createElement('b');
      name.textContent = person.name + (person.me ? ' · 나' : '');
      const sub = document.createElement('span');
      sub.textContent = person.host ? '방장' : person.job;
      label.append(name, sub);
      this.labels.append(label);
      ring.visible = !(person.me && this.mode === 'battle');
      this.people.push({ group, label, ring, name: person.name, me: person.me });
    });
    this.labels.dataset.count = roster.length;
    this.playerMap = this.map;
    this.playerMode = this.mode;
    if (this.mode === 'battle') this.syncPlayerCamera();
  }
  setInputEnabled(enabled) {
    this.inputEnabled = enabled;
    this.controls.enabled = enabled && this.mode !== 'battle';
    if (!enabled) {
      this.lookPointer = null;
      this.attackEffect.clear();
    }
  }
  attack(weapon) {
    return (
      this.ready && this.mode === 'battle' && this.inputEnabled && this.attackEffect.play(this.camera, weapon)
    );
  }
  syncPlayerCamera() {
    const me = this.people.find((p) => p.me);
    if (!me) return;
    // Mirrors Unity FollowCamera's 1.5 m pivot and StatManager's (0.3, 0.2, -1) offset.
    // A small amount of the local body intentionally remains in view.
    const giant = ['모노스탯 · 힘', '모노스탯 · 체력'].includes(this.roster.find((p) => p.me)?.job);
    const side = giant ? 0.35 : 0.3,
      height = giant ? 1.9 : 1.7;
    this.camera.rotation.set(this.lookPitch, this.lookYaw, 0, 'YXZ');
    const offset = new THREE.Vector3(side, 0, 1).applyEuler(this.camera.rotation);
    this.camera.position.copy(me.group.position).add(offset);
    this.camera.position.y += height;
    me.group.rotation.y = this.lookYaw + Math.PI;
  }
  effect(color = 0xcce5d3) {
    const me = this.people[0];
    if (!me) return;
    const ring = this.ring(0.8, color, me.group.position.x, me.group.position.z, 0.13);
    ring.material.transparent = true;
    this.effects.push({ ring, start: performance.now() });
  }
  move(dx, dz) {
    const me = this.people[0];
    if (!me) return;
    if (this.mode === 'battle') {
      const c = Math.cos(this.lookYaw),
        s = Math.sin(this.lookYaw);
      [dx, dz] = [dx * c + dz * s, -dx * s + dz * c];
    }
    const limit = ['arena', 'foundry'].includes(this.map) ? 13 : 8;
    me.group.position.x = THREE.MathUtils.clamp(me.group.position.x + dx, -limit, limit);
    me.group.position.z = THREE.MathUtils.clamp(me.group.position.z + dz, -limit, limit);
    me.group.rotation.y = Math.atan2(dx, dz);
    if (this.mode === 'battle') this.syncPlayerCamera();
  }
  applySettings(s) {
    this.quality = s.quality;
    this.land.traverse((item) => {
      if (item.name === 'Neon_Fill') item.visible = s.quality !== 'low';
    });
    this.renderer.setPixelRatio(
      Math.min(devicePixelRatio, s.quality === 'low' ? 1 : s.quality === 'medium' ? 1.3 : 1.8),
    );
    this.renderer.shadowMap.enabled = s.quality !== 'low';
    this.sun.castShadow = s.quality !== 'low';
    this.renderer.toneMappingExposure = (s.brightness / 100) * 1.35;
    this.controls.rotateSpeed = (s.sensitivity / 100) * 0.55;
    this.lookSensitivity = s.sensitivity / 100;
    this.controls.autoRotate = !s.motion;
    this.controls.autoRotateSpeed = 0.12;
    this.frameLimit = Number(s.fps);
    this.resize();
  }
  resize() {
    const rect = this.canvas.parentElement.getBoundingClientRect();
    this.camera.aspect = rect.width / rect.height;
    this.camera.updateProjectionMatrix();
    this.renderer.setSize(rect.width, rect.height, false);
  }
  animate(time) {
    if (document.hidden || time - this.lastFrame < 1000 / this.frameLimit - 1) return;
    this.lastFrame = time;
    this.attackEffect.update(time);
    if (this.mode !== 'battle') this.controls.update();
    for (const p of this.people) {
      const pos = new THREE.Vector3().copy(p.group.position);
      pos.y += 2.9;
      pos.project(this.camera);
      p.label.hidden = (this.mode === 'battle' && p.me) || pos.z > 1 || pos.z < -1;
      p.label.style.left = (pos.x * 0.5 + 0.5) * this.canvas.clientWidth + 'px';
      p.label.style.top = (-pos.y * 0.5 + 0.5) * this.canvas.clientHeight + 'px';
    }
    this.effects = this.effects.filter((e) => {
      const t = (time - e.start) / 750;
      if (t >= 1) {
        this.land.remove(e.ring);
        e.ring.geometry.dispose();
        e.ring.material.dispose();
        return false;
      }
      e.ring.scale.setScalar(1 + t * 4);
      e.ring.material.opacity = 1 - t;
      return true;
    });
    this.renderer.render(this.scene, this.camera);
  }
}
