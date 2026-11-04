from .cli import main

if __name__ == "__main__":  # spawned OCR workers may re-import the main module: never re-run the CLI
    raise SystemExit(main())
