## ADDED Requirements

### Requirement: Table-row entities

The extractor SHALL read equipment tables (weapons, armor, adventuring gear, tools, mounts and vehicles, trade goods,
poisons) into one entity per printed row, named from the row's label cell, with fields from the printed cells and the
category from the table's grouped sub-heading. When the book prints a description with the same name in the same
chapter section, its text SHALL become the entity's `entries`. A row whose cells cannot be read SHALL still be produced
and SHALL fail a check naming the row.

#### Scenario: Longsword row

- **WHEN** the PHB is extracted
- **THEN** `phb14.weapon.longsword` has `weaponCategory` "martial", `value` 1500, `weight` 3, `dmg1` "1d8", `dmgType`
  "S", the versatile property and `dmg2` "1d10", and all checks pass

#### Scenario: Unreadable row

- **WHEN** a row's cost cell OCRs as "1S gp"
- **THEN** the entity is produced, a `cost-valid` check fails naming the cell, and it is NeedsReview

### Requirement: Weapon and armor parsing

Weapons SHALL carry category, cost, damage dice and type, weight, properties and (for ranged or thrown) range. Armor
SHALL carry its type (light, medium, heavy, shield), cost, AC, Strength requirement, Stealth disadvantage and weight.

#### Scenario: Chain mail

- **WHEN** the PHB is extracted
- **THEN** `phb14.armor.chain-mail` has type "HA", `ac` 16, `strength` "13", `stealth` true, `value` 7500 and
  `weight` 55

### Requirement: Gear, vehicle and object parsing

Adventuring gear, tools, packs, mounts and trade goods SHALL be Item entities. Vehicles printed with statistics SHALL
be VehicleMount entities. Siege equipment and object stat blocks SHALL be Object entities with AC, HP and immunities as
printed.

#### Scenario: Gear with description

- **WHEN** the PHB is extracted
- **THEN** `phb14.item.crowbar` has `value` 200 and `weight` 5 from the table, and its "Using a crowbar grants
  advantage..." paragraph in `entries`

### Requirement: Trap, disease, poison and condition parsing

Traps and hazards SHALL be Trap entities with their detection and disarm DCs and effects as printed. Diseases SHALL be
DiseasePoison entities. Poisons SHALL be DiseasePoison entities with their type (contact, ingested, inhaled, injury)
and price. The PHB Appendix A conditions SHALL be Condition entities.

#### Scenario: Poison type

- **WHEN** the DMG is extracted
- **THEN** `dmg14.diseasepoison.burnt-othur-fumes` has `poisonTypes` ["inhaled"]

#### Scenario: Conditions

- **WHEN** the PHB is extracted
- **THEN** all 15 conditions are Condition entities, each passing `condition-known`

### Requirement: Equipment self-checks

Each type SHALL carry named checks with readable reasons naming the values, feeding the D4 disposition:

- `cost-valid`: a number with a cp/sp/ep/gp/pp unit, or a printed dash.
- `weight-valid`: a number with "lb.", or a printed dash.
- `damage-valid`: dice or a flat number, plus a damage type from the closed set.
- `properties-known`: every property is from the PHB property list.
- `ac-valid`: armor AC is a number, optionally "+ Dex modifier" with an optional "(max 2)".
- `trap-dcs-parsed`: printed DCs are numbers.
- `poison-type-known`: one of contact, ingested, inhaled or injury.
- `condition-known`: one of the 15 PHB conditions.

#### Scenario: Unknown property

- **WHEN** a weapon row's properties OCR as "Finesse, Lihgt"
- **THEN** `properties-known` fails naming "Lihgt" and the weapon is NeedsReview
