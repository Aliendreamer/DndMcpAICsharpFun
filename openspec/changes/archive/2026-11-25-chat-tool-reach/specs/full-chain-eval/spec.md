## ADDED Requirements

### Requirement: The full-chain eval SHALL run with one command and show the goal

`Tools/ModelEval/run-full.sh` SHALL build the tool, run it inside the app container so the admin key never leaves it, compare with the latest report, save the new report under `Tools/ModelEval/reports/` and remove its temporary files. The output SHALL show the share of passed cases next to the goal of 80%, the passed runs and the number of blank replies.

#### Scenario: The owner runs the eval

- **WHEN** `Tools/ModelEval/run-full.sh` finishes
- **THEN** the new report is in `Tools/ModelEval/reports/` and the passed share is printed beside the goal


### Requirement: Two question sets SHALL measure the chat, one of them never tuned on

The eval SHALL ship 50 reference questions and 50 cross-check questions with the same number per category and no question in common. Fixes SHALL be made from the reference set only. Every fact a question expects from a tool SHALL be present in the named entities of our own books, which `Tools/ModelEval/cases/check_cases.py` checks. `run-full.sh --set reference|crosscheck|all` SHALL run them, each with its own report history.

#### Scenario: A score is cross-checked

- **WHEN** both sets are run
- **THEN** a reference score well above the cross-check score shows that the fixes were fitted to the questions

#### Scenario: A question's fact is not in the books

- **WHEN** `check_cases.py` finds a tool fact missing from its evidence entities
- **THEN** it names the question and exits non-zero
