## ADDED Requirements

### Requirement: Letter-spaced caps headings are normalized at mapping time
`HeadingDespacer.Normalize` SHALL collapse stray 1–2 letter uppercase fragments in all-caps headings by merging them with adjacent fragments (e.g. `ABER R ATIONS` → `ABERRATIONS`, `OPTIONAL C LASS FEATURES` → `OPTIONAL CLASS FEATURES`, `TH E WAR R IOR` → `THE WARRIOR`). Fragments that are legitimate standalone words (`A`, `I`, `OF`, `TO`, `IN`, `ON`, `AT`, `BY`, `OR`, `AN`, `AS`, `IT`, `IS`, `BE`, `DO`, `NO`, `SO`, `UP`, `WE`, and dice tokens `D4`–`D20`) SHALL NOT be merged. Mixed-case or non-garbled headings SHALL pass through unchanged. The converter SHALL apply the despacer to every heading item's text.

#### Scenario: Spaced caps heading collapsed

- **WHEN** a heading item has text `H U MANOIDS`
- **THEN** the normalized text is `HUMANOIDS`

#### Scenario: Legitimate short words preserved

- **WHEN** a heading item has text `PATH OF THE BEAST`
- **THEN** the text is unchanged

#### Scenario: Mixed-case headings untouched

- **WHEN** a heading item has text `Animating Performance`
- **THEN** the text is unchanged
