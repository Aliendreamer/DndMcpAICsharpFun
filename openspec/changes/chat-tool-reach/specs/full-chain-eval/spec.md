## ADDED Requirements

### Requirement: The full-chain eval SHALL run with one command and show the goal

`Tools/ModelEval/run-full.sh` SHALL build the tool, run it inside the app container so the admin key never leaves it, compare with the latest report, save the new report under `Tools/ModelEval/reports/` and remove its temporary files. The output SHALL show the share of passed cases next to the goal of 80%, the passed runs and the number of blank replies.

#### Scenario: The owner runs the eval

- **WHEN** `Tools/ModelEval/run-full.sh` finishes
- **THEN** the new report is in `Tools/ModelEval/reports/` and the passed share is printed beside the goal

