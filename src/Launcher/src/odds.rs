//! Case opening and Trade Up odds, as close to CS2 as is publicly known:
//! - Weapon cases: Mil-Spec 79.92%, Restricted 15.98%, Classified 3.20%, Covert 0.64%, rare special item 0.26%
//!   (Valve's published odds). The grade is picked first, then an item of that grade.
//! - Other containers: each grade five times rarer than the one below, the same steps Valve uses.
//! - Knife finishes are equally likely; Doppler phases follow the community-measured split.
//! - Float: a wear band first (FN 3%, MW 24%, FT 33%, WW 24%, BS 16%), a uniform value in it, squeezed into
//!   the skin's own float range. Pattern 0-1000. StatTrak 10% in weapon cases (not gloves).

use crate::itemdb::{CaseInfo, CaseItem};
use rand::Rng;

pub const GRADES: [&str; 12] = [
    "Consumer Grade", "Base Grade", "Industrial Grade", "Mil-Spec Grade", "High Grade", "Restricted", "Remarkable",
    "Classified", "Exotic", "Covert", "Extraordinary", "Contraband",
];

const WEAPON_CASE_ODDS: [(&str, f64); 4] = [("Mil-Spec Grade", 0.7992), ("Restricted", 0.1598), ("Classified", 0.0320), ("Covert", 0.0064)];
pub const RARE_CHANCE: f64 = 0.0026;
const WEAR_BANDS: [(f64, f64, f64); 5] = [(0.00, 0.07, 0.03), (0.07, 0.15, 0.24), (0.15, 0.38, 0.33), (0.38, 0.45, 0.24), (0.45, 1.00, 0.16)];
const COMMON_PHASE: f64 = 24.25;

fn phase_weight(phase: Option<&str>) -> f64 {
    match phase {
        Some("Ruby" | "Sapphire" | "Black Pearl") => 1.0,
        Some("Emerald") => 3.0,
        _ => COMMON_PHASE,
    }
}

pub fn rank(grade: &str) -> usize {
    GRADES.iter().position(|g| *g == grade).unwrap_or(0)
}

/// Each grade in the container with its chance (and the rare special item's, last).
pub fn odds(case: &CaseInfo) -> Vec<(String, f64)> {
    let mut grades: Vec<String> = vec![];
    for i in &case.items {
        if !grades.contains(&i.rarity) {
            grades.push(i.rarity.clone());
        }
    }
    grades.sort_by_key(|g| rank(g));
    let rare = !case.rare.is_empty();
    let weapon_case = case.kind == "Case" && grades.iter().all(|g| WEAPON_CASE_ODDS.iter().any(|o| o.0 == g));
    let mut list: Vec<(String, f64)> = if weapon_case {
        grades.iter().map(|g| (g.clone(), WEAPON_CASE_ODDS.iter().find(|o| o.0 == g).unwrap().1)).collect()
    } else {
        // Five times rarer per grade up; a rare pool counts as one more grade.
        let steps = grades.len() + rare as usize;
        grades.iter().enumerate().map(|(i, g)| (g.clone(), 5f64.powi((steps - 1 - i) as i32))).collect()
    };
    if rare {
        list.push(("Rare".into(), if weapon_case { RARE_CHANCE } else { 1.0 }));
    }
    let total: f64 = list.iter().map(|x| x.1).sum();
    list.into_iter().map(|(g, w)| (g, w / total)).collect()
}

/// Walks the weights down from `roll`; the last entry catches rounding.
pub fn pick<'a, T>(items: &'a [T], weight: impl Fn(&T) -> f64, mut roll: f64) -> &'a T {
    for item in items {
        roll -= weight(item);
        if roll < 0.0 {
            return item;
        }
    }
    items.last().unwrap()
}

/// One drop: the item, and whether it came from the rare pool.
pub fn roll(case: &CaseInfo, rng: &mut impl Rng) -> (CaseItem, bool) {
    let o = odds(case);
    let grade = &pick(&o, |x| x.1, rng.gen::<f64>()).0;
    if grade == "Rare" {
        return (roll_rare(&case.rare, rng), true);
    }
    let pool: Vec<&CaseItem> = case.items.iter().filter(|i| &i.rarity == grade).collect();
    (pool[rng.gen_range(0..pool.len())].clone(), false)
}

/// Knife / glove finishes are equally likely; then the Doppler phase by its own odds.
fn roll_rare(rare: &[CaseItem], rng: &mut impl Rng) -> CaseItem {
    let mut names: Vec<&str> = vec![];
    for r in rare {
        if !names.contains(&r.name.as_str()) {
            names.push(&r.name);
        }
    }
    let name = names[rng.gen_range(0..names.len())];
    let finish: Vec<&CaseItem> = rare.iter().filter(|r| r.name == name).collect();
    if finish.len() == 1 {
        return finish[0].clone();
    }
    let total: f64 = finish.iter().map(|i| phase_weight(i.phase.as_deref())).sum();
    (*pick(&finish, |i| phase_weight(i.phase.as_deref()), rng.gen::<f64>() * total)).clone()
}

/// A float like CS2 makes one: wear band first, then into the skin's range.
pub fn roll_wear(min: f32, max: f32, rng: &mut impl Rng) -> f32 {
    let band = pick(&WEAR_BANDS, |b| b.2, rng.gen::<f64>());
    let value = band.0 + rng.gen::<f64>() * (band.1 - band.0);
    (min as f64 + value * (max - min) as f64).clamp(0.000001, 0.999999) as f32
}

/// The ticket rule: every input adds one ticket for each next-grade item of its own collection, and one ticket
/// wins. Each ticket comes back with its chance (an item from two inputs' collections is listed twice).
pub fn tickets<T: Clone>(outcomes_per_input: &[Vec<T>]) -> Vec<(T, f64)> {
    let all: Vec<T> = outcomes_per_input.iter().flatten().cloned().collect();
    let n = all.len() as f64;
    all.into_iter().map(|t| (t, 1.0 / n)).collect()
}

/// CS2's current float: each input's float put on its own skin's range, averaged, placed on the new range.
pub fn trade_float(inputs: &[(f32, f32, f32)], min: f32, max: f32) -> f32 {
    let normalized: f32 = inputs.iter().map(|&(w, lo, hi)| if hi > lo { ((w - lo) / (hi - lo)).clamp(0.0, 1.0) } else { 0.0 }).sum::<f32>() / inputs.len() as f32;
    ((min + normalized * (max - min)) as f64).clamp(0.000001, 0.999999) as f32
}

#[cfg(test)]
mod tests {
    use super::*;
    use rand::SeedableRng;

    fn item(rarity: &str, name: &str) -> CaseItem {
        CaseItem { id: format!("skin-{name}"), name: name.into(), paint: 1, rarity: rarity.into(), ..Default::default() }
    }

    #[test]
    fn weapon_case_odds_are_valves() {
        let case = CaseInfo { kind: "Case".into(), items: ["Mil-Spec Grade", "Restricted", "Classified", "Covert"].iter().map(|r| item(r, "x")).collect(),
            rare: vec![item("Covert", "knife")], ..Default::default() };
        let o = odds(&case);
        assert!((o.iter().map(|x| x.1).sum::<f64>() - 1.0).abs() < 1e-9);
        assert!((o.iter().find(|x| x.0 == "Rare").unwrap().1 - 0.0026).abs() < 1e-4);
        assert!(o[0].1 > 0.79);
    }

    #[test]
    fn capsule_grades_are_five_times_rarer() {
        let case = CaseInfo { kind: "Sticker Capsule".into(), items: vec![item("High Grade", "a"), item("Remarkable", "b")], ..Default::default() };
        let o = odds(&case);
        assert!((o[0].1 / o[1].1 - 5.0).abs() < 1e-9);
    }

    #[test]
    fn wear_stays_in_the_skins_range() {
        let mut rng = rand::rngs::StdRng::seed_from_u64(1);
        assert!((0..1000).all(|_| (0.06..=0.8).contains(&roll_wear(0.06, 0.8, &mut rng))));
    }

    #[test]
    fn trade_up_tickets_and_float() {
        let per: Vec<Vec<&str>> = std::iter::repeat(vec!["a1", "a2"]).take(7).chain(std::iter::repeat(vec!["b1", "b2", "b3", "b4"]).take(3)).collect();
        let t = tickets(&per);
        let a: f64 = t.iter().filter(|x| x.0.starts_with('a')).map(|x| x.1).sum();
        assert!((a - 14.0 / 26.0).abs() < 1e-9);
        assert!((trade_float(&[(0.5, 0.0, 1.0), (0.2, 0.1, 0.3)], 0.06, 0.8) - 0.43).abs() < 1e-4);
    }
}
