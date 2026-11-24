import * as THREE from 'https://cdn.jsdelivr.net/npm/three@0.160.0/build/three.module.min.js';
const stage = document.getElementById('stage3d');
const canvas = document.getElementById('stage3d-canvas');
if (stage && canvas) {
  const mobile = matchMedia('(max-width: 800px)').matches;
  const renderer = new THREE.WebGLRenderer({ canvas, alpha: true, antialias: !mobile, powerPreference: 'low-power' });
  renderer.setPixelRatio(Math.min(devicePixelRatio || 1, mobile ? 1.25 : 1.75));
  const scene = new THREE.Scene();
  const camera = new THREE.PerspectiveCamera(38, 1, 0.1, 100);
  camera.position.set(0, 0, 9);

  const css = () => getComputedStyle(document.documentElement);
  const col = (v, f) => new THREE.Color((css().getPropertyValue(v) || f).trim() || f);
  const group = new THREE.Group(); scene.add(group);

  const coreMat = new THREE.MeshStandardMaterial({ color: 0x3157d5, roughness: .35, metalness: .1, flatShading: true });
  const core = new THREE.Mesh(new THREE.IcosahedronGeometry(1.55, 1), coreMat);
  const wire = new THREE.Mesh(new THREE.IcosahedronGeometry(1.95, 1), new THREE.MeshBasicMaterial({ color: 0x7ea0ff, wireframe: true, transparent: true, opacity: .35 }));
  group.add(core, wire);

  // orbiting "freelancers": small shapes on two rings + one coin (EGP)
  const orbit = new THREE.Group(); group.add(orbit);
  const shapes = [];
  const geos = [new THREE.OctahedronGeometry(.32), new THREE.TorusGeometry(.26, .1, 10, 24), new THREE.BoxGeometry(.42, .42, .42), new THREE.SphereGeometry(.28, 18, 14), new THREE.TetrahedronGeometry(.38)];
  const palette = [0x7ea0ff, 0xffffff, 0x3157d5, 0x9ab4ff, 0x1fa971];
  for (let i = 0; i < 9; i++) {
    const m = new THREE.Mesh(geos[i % geos.length], new THREE.MeshStandardMaterial({ color: palette[i % palette.length], roughness: .35, metalness: .25, flatShading: true }));
    const r = 3.0 + (i % 3) * .55, a = (i / 9) * Math.PI * 2, tilt = (i % 2 ? .5 : -.35);
    m.userData = { r, a, tilt, s: .25 + (i % 4) * .08, spin: .4 + i * .07 };
    orbit.add(m); shapes.push(m);
  }
  const coin = new THREE.Mesh(new THREE.CylinderGeometry(.55, .55, .12, 40), new THREE.MeshStandardMaterial({ color: 0xf0c050, emissive: 0x6b4a00, roughness: .3, metalness: .35 }));
  coin.rotation.x = Math.PI / 2; const coinG = new THREE.Group(); coinG.add(coin); coinG.position.set(2.7, 1.2, .8); group.add(coinG);

  // particles
  const N = mobile ? 90 : 220, pos = new Float32Array(N * 3);
  for (let i = 0; i < N; i++) { const r = 4 + Math.random() * 5, t = Math.random() * 6.283, p = Math.acos(2 * Math.random() - 1); pos[i * 3] = r * Math.sin(p) * Math.cos(t); pos[i * 3 + 1] = r * Math.sin(p) * Math.sin(t) * .6; pos[i * 3 + 2] = r * Math.cos(p) - 2; }
  const pg = new THREE.BufferGeometry(); pg.setAttribute('position', new THREE.BufferAttribute(pos, 3));
  const pm = new THREE.PointsMaterial({ size: .045, color: 0x7ea0ff, transparent: true, opacity: .8, sizeAttenuation: true });
  const pts = new THREE.Points(pg, pm); scene.add(pts);

  scene.add(new THREE.AmbientLight(0xffffff, 1.2));
  const key = new THREE.DirectionalLight(0xffffff, 2.1); key.position.set(4, 5, 6); scene.add(key);
  const rim = new THREE.PointLight(0x7ea0ff, 40, 20); rim.position.set(-5, -2, 3); scene.add(rim);

  function theme() {
    const dark = document.documentElement.getAttribute('data-theme') === 'dark';
    coreMat.color.set(dark ? 0x5a7cf0 : 0x3157d5);
    wire.material.color.set(dark ? 0x9ab4ff : 0x3157d5); wire.material.opacity = dark ? .4 : .3;
    pm.color.set(dark ? 0x9ab4ff : 0x3157d5); pm.opacity = dark ? .8 : .55;
    rim.intensity = dark ? 60 : 30;
  }
  theme();
  new MutationObserver(theme).observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] });

  let w = 0, h = 0;
  function size() {
    const r = stage.getBoundingClientRect(); w = r.width; h = r.height;
    renderer.setSize(w, h, false); camera.aspect = w / h; camera.position.z = w / h < 1.3 ? 12 : 9; camera.updateProjectionMatrix();
  }
  size(); new ResizeObserver(size).observe(stage);

  let tx = 0, ty = 0, cx = 0, cy = 0, dragX = 0, dragging = false, lastX = 0, hover = 0, boost = 0;
  const rect = () => stage.getBoundingClientRect();
  stage.addEventListener('pointermove', e => {
    const r = rect(); tx = ((e.clientX - r.left) / r.width - .5) * 2; ty = ((e.clientY - r.top) / r.height - .5) * 2;
    if (dragging) { dragX += (e.clientX - lastX) * .01; lastX = e.clientX; }
  });
  stage.addEventListener('pointerenter', () => hover = 1);
  stage.addEventListener('pointerleave', () => { hover = 0; tx = ty = 0; });
  stage.addEventListener('pointerdown', e => { dragging = true; lastX = e.clientX; boost = 1; stage.classList.add('grab'); });
  addEventListener('pointerup', () => { dragging = false; stage.classList.remove('grab'); });
  stage.addEventListener('click', () => { boost = 1; });

  let running = false, raf = 0, t0 = performance.now();
  function frame(now) {
    raf = 0; if (!running) return;
    const t = (now - t0) / 1000;
    cx += (tx - cx) * .06; cy += (ty - cy) * .06; boost *= .95;
    group.rotation.y = t * .25 + dragX + cx * .5; group.rotation.x = cy * .3 + Math.sin(t * .6) * .08;
    core.rotation.y = t * (.3 + boost * 2); core.rotation.x = t * .2;
    wire.rotation.y = -t * .15; wire.rotation.z = t * .1;
    const sc = 1 + hover * .06 + boost * .12; core.scale.setScalar(sc);
    shapes.forEach((m, i) => { const d = m.userData, a = d.a + t * d.s * (1 + boost); m.position.set(Math.cos(a) * d.r, Math.sin(a * 1.3) * 1.1 + d.tilt * Math.sin(a), Math.sin(a) * d.r * .5); m.rotation.x += .01 * d.spin; m.rotation.y += .013 * d.spin; });
    coinG.rotation.y = t * 1.2; coinG.position.y = 1.2 + Math.sin(t * 1.4) * .25;
    pts.rotation.y = t * .03;
    renderer.render(scene, camera);
    raf = requestAnimationFrame(frame);
  }
  const start = () => { if (!running) { running = true; raf = requestAnimationFrame(frame); } };
  const stop = () => { running = false; };
  new IntersectionObserver(es => es[0].isIntersecting && !document.hidden ? start() : stop(), { threshold: .05 }).observe(stage);
  document.addEventListener('visibilitychange', () => document.hidden ? stop() : (stage.getBoundingClientRect().bottom > 0 && start()));
  canvas.addEventListener('webglcontextlost', e => { e.preventDefault(); stop(); stage.classList.remove('live'); });
  stage.classList.add('live');
}
