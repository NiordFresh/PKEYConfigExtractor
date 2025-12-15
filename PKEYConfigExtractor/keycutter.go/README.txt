The main reason I decided to rewrite the algorithm from awuctl's script based on Python to GOlang was, among other things, performance, which was embarrassingly poor in Python...

The program generates and decodes Windows product keys using the 2009 algorithm.  
The key format is: XXXXX-XXXXX-XXXXX-XXXXX-XXXXX (25 characters + 4 hyphens)
-----------------
KEY STRUCTURE:
The 115-bit key consists of:
- Group (20 bits) – Reference group ID
- Serial (30 bits) – Serial number
- Security (54 bits) – Security value
- Checksum (10 bits) – CRC32 checksum (truncated to 10 bits)
- Upgrade (1 bit) – Upgrade flag
- Extra (1 bit) – Extra flag
-----------------
USAGE:
1. GENERATING A KEY
"go run keycutter.go encode <group> <serial> <security> [options]"

Example:
"go run keycutter.go encode 1801 237 24"
Output: `NTWG2-8X36D-9PWW4-XG86C-V8MRC`

Options:
- `-u <value>` – Upgrade bit (0 or 1, default: 0)
- `-c <value>` – Checksum (0x400 for automatic, default: 0x400)
- `-e <value>` – Extra bit (0 or 1, default: 0)

2. DECODING A KEY
"go run keycutter.go decode <key> [options]"

Example:
"go run keycutter.go decode NTWG2-8X36D-9PWW4-XG86C-V8MRC"


Options:
- `-output <format>` – Output format: `parametric`, `raw`, `rawhex` (default: `parametric`)
-----------------
ALGORITHM:
1. Key built using bitwise OR and shift operations (`<<`)
2. Checksum: CRC32/MPEG-2 truncated to 10 bits
3. Conversion to base-24 (alphabet: `BCDFGHJKMPQRTVWXY2346789`)
4. Position of 'N' determined by the first byte of encoding
5. Hyphens inserted every 5 characters
-----------------
LIMITS:
- Group: max `0xFFFFF` (1,048,575)
- Serial: max `0x3FFFFFFF` (1,073,741,823)
- Security: max `0x1FFFFFFFFFFFFF` (9,007,199,254,740,991)
- Checksum: max `0x3FF` (1,023)
- Upgrade/Extra: 0 or 1

