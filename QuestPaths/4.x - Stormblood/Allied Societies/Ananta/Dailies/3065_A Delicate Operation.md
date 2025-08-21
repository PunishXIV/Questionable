## A Delicate Operation

```
0 xx xx    0 0 0
  48 16(1)       → 2009369         2009367         2009365
  48 32(2)       →                 2009367 2009366 2009365
  48 48(3)       → 2009369 2009368                 2009365
  48 64(4)       → 2009369         2009367 2009366
  48 80(5)       →         2009368 2009367 2009366

Seems like an unusual distribution; but couldn't find any value > 80 in testing.

Completion flags:
    2009369 → 8
    2009368 → 16
    2009367 → 32
    2009366 → 64
    2009365 → 128
```
