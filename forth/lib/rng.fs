\ rng.fs — 16-bit LCG random number generator
\
\ Provides: seed (variable), rng, rnd
\ Requires: kernel primitives *, +, -, C@, AND
\
\ rng updates the seed using the LCG formula: seed = seed * 25173 + 13849
\ Full period 65536. Good multiplier gives well-distributed high byte.
\
\ rnd returns 0..n-1 for power-of-2 n, using the high byte of seed
\ (best bits in an LCG) masked with AND.

VARIABLE seed

: rng   ( -- )  seed @  25173 *  13849 +  seed ! ;
: rnd   ( n -- 0..n-1 )  rng  1 -  seed C@ AND ;
