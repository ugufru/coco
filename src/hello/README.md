# Hello World

The smallest program that proves the toolchain works: it prints

```
BARE NAKED FORTH 1.2
```

on the text screen and waits. BREAK returns to BASIC.

```
make run    # build hello.bin and run it in XRoar
```

The version comes from the kernel itself: `fc.py` exposes the kernel's
`KERN_VERSION` (`$0102`, major and minor in BCD) as the Forth constant
`KERN-VERSION`, and `version` prints its two bytes as `1.2`. So if the
banner shows the wrong number, the kernel and the program disagree about
which kernel they were built with.

What it shows:

- Colon definitions built from kernel primitives only: `EMIT`, `CHAR`,
  `RSHIFT`, `AND`, `KEY`.
- A build-time constant from the kernel map (`KERN-VERSION`).
- `exit-basic` from `lib/bye.fs`: a clean return to the `OK` prompt in
  ROM mode.

It is the first program on `build/demos.dsk` (`LOADM"HELLO":EXEC`) and
the "Start Here" entry in the demo gallery (`tutorials/demos`).
