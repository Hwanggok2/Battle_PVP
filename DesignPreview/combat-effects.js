import * as THREE from 'three';

// Fixed-size meshes are reused for every swing; rapid clicks cannot grow the particle count.
export class BlockSlashEffect {
  constructor(scene) {
    this.root = new THREE.Group();
    this.root.visible = false;
    scene.add(this.root);
    this.geometry = new THREE.BoxGeometry(1, 1, 1);
    this.coreMaterial = new THREE.MeshBasicMaterial({
      color: 0xffffff,
      transparent: true,
      depthWrite: false,
      toneMapped: false,
    });
    this.glowMaterial = new THREE.MeshBasicMaterial({
      color: 0x42e9fa,
      transparent: true,
      opacity: 0.3,
      depthWrite: false,
      blending: THREE.AdditiveBlending,
      toneMapped: false,
    });
    this.count = 36;
    this.core = new THREE.InstancedMesh(this.geometry, this.coreMaterial, this.count);
    this.glow = new THREE.InstancedMesh(this.geometry, this.glowMaterial, this.count);
    for (const mesh of [this.core, this.glow]) {
      mesh.instanceMatrix.setUsage(THREE.DynamicDrawUsage);
      mesh.frustumCulled = false;
      this.root.add(mesh);
    }
    this.light = new THREE.PointLight(0x58e8ff, 0, 5, 2);
    this.light.position.set(0, -0.4, -1.8);
    this.root.add(this.light);
    this.part = new THREE.Object3D();
    this.serial = 0;
    this.end = 0;
  }
  play(camera, weapon = '검') {
    const now = performance.now();
    if (now < this.nextAttack) return false;
    this.nextAttack = now + 240;
    this.start = now;
    this.end = now + 460;
    this.serial++;
    this.bow = weapon === '활';
    this.root.position.copy(camera.position);
    this.root.quaternion.copy(camera.quaternion);
    this.root.visible = true;
    this.update(now);
    return true;
  }
  clear() {
    this.root.visible = false;
    this.end = 0;
    this.nextAttack = 0;
    this.light.intensity = 0;
  }
  update(now) {
    if (!this.root.visible) return;
    const age = (now - this.start) / 1000;
    if (now >= this.end) {
      this.clear();
      return;
    }
    const flip = this.serial % 2 ? 1 : -1;
    for (let i = 0; i < this.count; i++) {
      const trail = i < 20,
        u = trail ? i / 19 : (i - 20) / 15;
      const delay = trail ? u * 0.085 : 0.07;
      const life = age - delay;
      const fade = life < 0 ? 0 : Math.max(0, 1 - life / (trail ? 0.3 : 0.36));
      let x, y, z, sx, sy, rotation;
      if (trail) {
        x = flip * (1.05 - 2.05 * u);
        y = 0.35 - 0.68 * u + 0.25 * Math.sin(u * Math.PI);
        z = -1.5 - 0.25 * Math.sin(u * Math.PI);
        sx = 0.13 + 0.035 * Math.sin(u * Math.PI);
        sy = 0.19 + 0.12 * Math.sin(u * Math.PI);
        rotation = flip * (-0.3 + u * 0.6);
      } else {
        const a = u * Math.PI * 2 + this.serial * 0.43;
        const speed = 0.55 + (i % 5) * 0.17;
        x = -flip * 0.48 + Math.cos(a) * (0.2 + Math.max(0, life) * speed * 2);
        y = -0.13 + Math.sin(a) * (0.16 + Math.max(0, life) * speed) - life * life;
        z = -1.65 - Math.max(0, life) * (0.5 + u);
        sx = sy = 0.045 + (i % 4) * 0.023;
        rotation = a + life * 3;
      }
      // Bow keeps the same square language, with a forward burst rather than a sword sweep.
      if (this.bow) {
        x *= 0.24;
        y *= 0.45;
        z -= age * 6;
      }
      this.part.position.set(x, y, z);
      this.part.rotation.set(trail ? 0 : life * 2, 0, rotation);
      this.part.scale.set(sx * fade, sy * fade, 0.035 * fade);
      this.part.updateMatrix();
      this.core.setMatrixAt(i, this.part.matrix);
      this.part.scale.multiplyScalar(2.4);
      this.part.updateMatrix();
      this.glow.setMatrixAt(i, this.part.matrix);
    }
    this.core.instanceMatrix.needsUpdate = this.glow.instanceMatrix.needsUpdate = true;
    this.coreMaterial.opacity = Math.min(1, Math.max(0, (0.43 - age) / 0.14));
    this.glowMaterial.opacity = this.coreMaterial.opacity * 0.28;
    this.light.intensity = 7 * Math.max(0, 1 - age / 0.18);
  }
}
