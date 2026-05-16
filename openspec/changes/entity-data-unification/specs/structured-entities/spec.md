## MODIFIED Requirements

### Requirement: Entity ID format uses source key slug as book prefix
All entity IDs SHALL follow the format `{sourcekeyslug}.{typeslug}.{nameslug}` where `sourcekeyslug` is the lowercase 5etools source key (with year suffix for editions where needed), `typeslug` is the lowercase `EntityType` name, and `nameslug` is the kebab-case entity name.

Edition slug conventions:

- PHB (2014): `phb14`
- PHB (2024 / XPHB): `phb24`
- DMG (2014): `dmg14`
- DMG (2024 / XDMG): `dmg24`
- MM (2014): `mm14`
- MM (2025): `mm24`
- TCE: `tce`
- XGTE: `xgte`
- MPMM: `mpmm`

#### Scenario: Tasha's subclass entity ID

- **WHEN** "Circle of Spores" (Druid subclass, TCE) is stored
- **THEN** its entity ID SHALL be `"tce.subclass.circle-of-spores"`

#### Scenario: PHB class entity ID

- **WHEN** "Fighter" (Class, PHB 2014) is stored
- **THEN** its entity ID SHALL be `"phb14.class.fighter"`

#### Scenario: Both pipelines produce the same ID

- **WHEN** 5etools import and canonical ingest both process "Circle of Spores" from TCE
- **THEN** both SHALL produce entity ID `"tce.subclass.circle-of-spores"`
- **THEN** the Qdrant collection SHALL contain exactly one point for this entity after both pipelines run
