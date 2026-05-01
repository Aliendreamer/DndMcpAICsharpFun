# Serena-First Rule

For all code files (.cs and any source files):
- **Read/discovery** → `get_symbols_overview`, then `find_symbol` with `include_body=true`. Built-in Read is FORBIDDEN for code files.
- **Edits** → `replace_symbol_body` or `replace_content`. Built-in Edit is FORBIDDEN for code files.
- **Search** → `find_referencing_symbols`, `search_for_pattern`. Not Bash grep on code files.

Non-code files (yaml, json config, docker-compose, .gitignore) may use built-in tools.

Always call `initial_instructions` at the start of any coding session before touching code.
