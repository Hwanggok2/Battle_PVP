import * as THREE from 'three';

const cyan = 0x20e5ff,
  pink = 0xff278f,
  violet = 0x9b55ff,
  amber = 0xffaf30;

// Fixed 5×7 facility lettering, merged into one geometry per sign (no font downloads).
const glyphs = {
  A: '01110/10001/10001/11111/10001/10001/10001',
  B: '11110/10001/10001/11110/10001/10001/11110',
  C: '01111/10000/10000/10000/10000/10000/01111',
  D: '11110/10001/10001/10001/10001/10001/11110',
  E: '11111/10000/10000/11110/10000/10000/11111',
  F: '11111/10000/10000/11110/10000/10000/10000',
  G: '01111/10000/10000/10111/10001/10001/01110',
  H: '10001/10001/10001/11111/10001/10001/10001',
  I: '11111/00100/00100/00100/00100/00100/11111',
  K: '10001/10010/10100/11000/10100/10010/10001',
  L: '10000/10000/10000/10000/10000/10000/11111',
  M: '10001/11011/10101/10101/10001/10001/10001',
  N: '10001/11001/11001/10101/10011/10011/10001',
  O: '01110/10001/10001/10001/10001/10001/01110',
  P: '11110/10001/10001/11110/10000/10000/10000',
  R: '11110/10001/10001/11110/10100/10010/10001',
  S: '01111/10000/10000/01110/00001/00001/11110',
  T: '11111/00100/00100/00100/00100/00100/00100',
  U: '10001/10001/10001/10001/10001/10001/01110',
  V: '10001/10001/10001/10001/10001/01010/00100',
  W: '10001/10001/10001/10101/10101/11011/10001',
  X: '10001/10001/01010/00100/01010/10001/10001',
  Y: '10001/10001/01010/00100/00100/00100/00100',
  0: '01110/10001/10011/10101/11001/10001/01110',
  1: '00100/01100/00100/00100/00100/00100/01110',
  2: '01110/10001/00001/00010/00100/01000/11111',
  7: '11111/00001/00010/00100/01000/01000/01000',
  8: '01110/10001/10001/01110/10001/10001/01110',
  ' ': '00000/00000/00000/00000/00000/00000/00000',
};

function lettering(text, width) {
  const unit = width / (text.length * 6 - 1),
    positions = [],
    normals = [];
  [...text].forEach((char, i) => {
    if (!glyphs[char]) throw new Error(`Unsupported sign character: ${char}`);
    glyphs[char].split('/').forEach((row, y) => {
      for (const run of row.matchAll(/1+/g)) {
        const g = new THREE.BoxGeometry((run[0].length - 0.14) * unit, unit * 0.86, 0.045).toNonIndexed();
        g.translate(-width / 2 + (i * 6 + run.index + run[0].length / 2) * unit, (3 - y) * unit, 0);
        positions.push(...g.attributes.position.array);
        normals.push(...g.attributes.normal.array);
        g.dispose();
      }
    });
  });
  const geometry = new THREE.BufferGeometry();
  geometry.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3));
  geometry.setAttribute('normal', new THREE.Float32BufferAttribute(normals, 3));
  return geometry;
}

// Local soft halos only; no full-screen bloom pass or flashing animation.
function halo(parent, color, width, height, x, y, z, opacity = 0.3, floor = false) {
  const mesh = new THREE.Mesh(
    new THREE.PlaneGeometry(width, height),
    new THREE.ShaderMaterial({
      uniforms: { tint: { value: new THREE.Color(color) }, strength: { value: opacity } },
      vertexShader:
        'varying vec2 vUv; void main(){vUv=uv; gl_Position=projectionMatrix*modelViewMatrix*vec4(position,1.0);}',
      fragmentShader:
        'varying vec2 vUv; uniform vec3 tint; uniform float strength; void main(){vec2 p=(vUv-.5)*2.; float a=pow(max(0.,1.-dot(p,p)),2.); gl_FragColor=vec4(tint,a*strength);\n#include <colorspace_fragment>\n}',
      transparent: true,
      blending: THREE.AdditiveBlending,
      depthWrite: false,
      side: THREE.DoubleSide,
    }),
  );
  mesh.position.set(x, y, z);
  if (floor) mesh.rotation.x = -Math.PI / 2;
  mesh.userData.previewOnly = true;
  mesh.name = 'Neon_SoftGlow';
  parent.add(mesh);
}

function sign(world, text, x, y, z, width, color, rotation = 0, accent = cyan) {
  const group = new THREE.Group(),
    height = Math.max(1.05, (width / text.length) * 1.7);
  group.name = `Neon_${text.replaceAll(' ', '_')}`;
  group.position.set(x, y, z);
  group.rotation.y = rotation;
  group.userData.neonSign = { text, color: '#' + color.toString(16).padStart(6, '0') };
  world.land.add(group);
  const lightMaterial = new THREE.MeshBasicMaterial({ color, toneMapped: false });
  world.box(width + 0.6, height + 0.4, 0.23, 0, 0, 0, 0x091021, group);
  for (const sy of [-1, 1]) {
    const edge = world.box(
      width + 0.65,
      0.045,
      0.055,
      0,
      sy * (height / 2 + 0.2),
      0.15,
      lightMaterial,
      group,
    );
    edge.castShadow = false;
  }
  const letters = new THREE.Mesh(lettering(text, width - 0.1), lightMaterial);
  letters.position.z = 0.15;
  letters.name = 'Luminous_Lettering';
  group.add(letters);
  const status = world.box(
    0.075,
    height * 0.75,
    0.06,
    -width / 2 - 0.18,
    0,
    0.16,
    new THREE.MeshBasicMaterial({ color: accent, toneMapped: false }),
    group,
  );
  status.castShadow = false;
  halo(group, color, width * 1.3, height * 3.6, 0, 0, -0.14, 0.26);
  halo(group, color, width * 1.1, height * 1.8, 0, 0, 0.19, 0.13);
  return group;
}

function lightPool(world, color, x, z, intensity = 65) {
  const light = new THREE.PointLight(color, intensity, 13, 2);
  light.position.set(x, 2.8, z);
  light.name = 'Neon_Fill';
  light.visible = world.quality !== 'low';
  world.land.add(light);
  halo(world.land, color, 7, 8, x, 0.018, z, 0.11, true);
}

export function addMapNeon(world, map) {
  if (map === 'lobby') {
    sign(world, 'CITY LINK', -6.6, 5.1, -9.45, 8.1, pink, 0, violet);
    sign(world, 'OPS 01', 6.3, 5.1, -9.45, 6.1, cyan);
    sign(world, 'ARMORY', -12.55, 3.6, -0.5, 4.7, amber, Math.PI / 2);
    sign(world, 'CORE', 12.55, 3.6, -0.5, 4.3, violet, -Math.PI / 2, pink);
    lightPool(world, pink, -8, -5);
    lightPool(world, cyan, 8, -5);
    lightPool(world, amber, -10, 2, 45);
    lightPool(world, violet, 10, 2, 50);
  } else if (map === 'waiting') {
    sign(world, 'HANGAR 08', 0, 7.65, -6.85, 9, amber, 0, pink);
    sign(world, 'ARMORY', -7.9, 4.8, -7.05, 4.9, cyan);
    sign(world, 'LINK', 7.9, 4.8, -7.05, 4, pink, 0, violet);
    sign(world, 'EXIT', 10.82, 3.8, 3.5, 3.7, violet, -Math.PI / 2);
    lightPool(world, cyan, -8, -2);
    lightPool(world, pink, 8, -2);
    lightPool(world, amber, 0, -7, 45);
    lightPool(world, violet, 8, 5, 45);
  } else if (map === 'arena') {
    sign(world, 'SECTOR 07', -8.5, 5.8, -13.9, 8.1, pink, 0, amber);
    sign(world, 'LAB 02', 8.5, 5.8, -13.9, 7.2, cyan);
    sign(world, 'POWER', -14.6, 4.15, 3.5, 5.1, amber, Math.PI / 2);
    sign(world, 'CORE', 14.6, 4.15, -1, 4.6, violet, -Math.PI / 2, pink);
    for (const x of [-15, 15]) world.strip(0.12, 0.13, 27, x, 5.3, 0, x < 0 ? pink : violet);
    lightPool(world, pink, -10, -10);
    lightPool(world, cyan, 10, -10);
    lightPool(world, amber, -12, 4, 60);
    lightPool(world, violet, 12, 4, 75);
  } else if (map === 'foundry') {
    sign(world, 'NOVA', -29, 6.3, -19.8, 7.8, violet, 0, pink);
    sign(world, 'PULSE', 25, 8.4, -22.8, 7.7, pink, 0, amber);
    for (const [x, text, color] of [
      [-9, 'ROOF 07', cyan],
      [7.5, 'NIGHT LINE', pink],
    ]) {
      sign(world, text, x, 5.5, -14.7, 8, color, 0, amber);
      for (const dx of [-3.1, 3.1]) world.box(0.17, 4.7, 0.2, x + dx, 2.9, -14.9, 0x26394f);
    }
    sign(world, 'POWER', -15.65, 3.4, -2, 4.4, amber, Math.PI / 2);
    sign(world, 'LINK', 15.65, 3.4, 2, 4.2, violet, -Math.PI / 2);
    for (const [x, z, color] of [
      [-29, -25, violet],
      [25, -28, pink],
      [-34, 8, cyan],
      [32, 6, amber],
    ]) {
      world.strip(0.14, 18, 0.1, x - 3.6, -5, z + 5.1, color);
      world.strip(0.14, 18, 0.1, x + 3.6, -5, z + 5.1, color);
    }
    lightPool(world, cyan, -10, -11);
    lightPool(world, pink, 10, -11);
    lightPool(world, amber, -13, 2, 55);
    lightPool(world, violet, 13, 4, 75);
  }
}
