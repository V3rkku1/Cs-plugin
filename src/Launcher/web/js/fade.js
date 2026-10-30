// Fade % from the pattern seed, as the community rates it (port of chescos/csgo-fade-percentage-calculator, MIT):
// Valve's random number generator, seeded with the pattern, picks the paint's offset and rotation; the most
// faded seed of a weapon is 100%, the least 80%.

const FADE = { x: [-0.7, -0.7], y: [-0.7, -0.7], r: [-55, -65] };
const CONFIGS = {
  Fade: {
    weapons: ["AWP", "Bayonet", "Bowie Knife", "Butterfly Knife", "Classic Knife", "Falchion Knife", "Flip Knife", "Glock-18", "Gut Knife",
      "Huntsman Knife", "Karambit", "Kukri Knife", "M4A1-S", "M9 Bayonet", "MAC-10", "MP7", "Navaja Knife", "Nomad Knife", "Paracord Knife",
      "R8 Revolver", "Shadow Daggers", "Skeleton Knife", "Stiletto Knife", "Survival Knife", "Talon Knife", "UMP-45", "Ursus Knife"],
    reversed: ["AWP", "Karambit", "MP7", "Talon Knife"],
    config: { MP7: { x: [-0.9, -0.3], y: [-0.7, -0.5], r: [-55, -65] }, "M4A1-S": { x: [-0.14, 0.05], y: [0, 0], r: [-45, -45] } },
  },
  "Amber Fade": { weapons: ["AUG", "Galil AR", "MAC-10", "P2000", "R8 Revolver", "Sawed-Off"], reversed: [], config: {} },
  "Acid Fade": { weapons: ["SSG 08"], reversed: ["SSG 08"], config: { "SSG 08": { x: [-2.4, -2.1], y: [0, 0], r: [-55, -65] } } },
};

/** Valve's uniform random stream (Park–Miller with a shuffle table), as the game seeds patterns with it. */
function rng(seed) {
  const NTAB = 32, IA = 16807, IM = 2147483647, IQ = 127773, IR = 2836, NDIV = 1 + (IM - 1) / NTAB;
  let idum = seed >= 0 ? -seed : seed, iy = 0;
  const iv = [];
  const step = () => { const k = Math.floor(idum / IQ); idum = IA * (idum - k * IQ) - IR * k; if (idum < 0) idum += IM; };
  const next = () => {
    if (idum <= 0 || iy === 0) {
      idum = -idum < 1 ? 1 : -idum;
      for (let j = NTAB + 7; j >= 0; j--) { step(); if (j < NTAB) iv[j] = idum; }
      iy = iv[0];
    }
    step();
    const j = Math.floor(iy / NDIV);
    iy = iv[j];
    iv[j] = idum;
    return iy;
  };
  return (lo, hi) => Math.min(next() / IM, 1 - 1.2e-7) * (hi - lo) + lo;
}

const memo = new Map();
function percentages(finish, weapon) {
  const key = `${finish}|${weapon}`;
  if (memo.has(key)) return memo.get(key);
  const { config, reversed } = CONFIGS[finish];
  const c = config[weapon] ?? FADE;
  const raw = [];
  for (let seed = 0; seed <= 1000; seed++) {
    const rand = rng(seed);
    const x = rand(...c.x);
    rand(...c.y);
    const r = rand(...c.r);
    const rot = c.r[0] !== c.r[1], off = c.x[0] !== c.x[1];
    raw.push(rot && off ? r * x : rot ? r : x);
  }
  const [best, worst] = reversed.includes(weapon) ? [Math.min(...raw), Math.max(...raw)] : [Math.max(...raw), Math.min(...raw)];
  const list = raw.map(v => 80 + (worst - v) / (worst - best) * 20);
  memo.set(key, list);
  return list;
}

/** "Karambit", "Fade", 412 gives the fade %, or null for a finish or weapon that has none. */
export function fadePercent(weapon, finish, seed) {
  return CONFIGS[finish]?.weapons.includes(weapon) && seed >= 0 && seed <= 1000 ? percentages(finish, weapon)[seed] : null;
}
