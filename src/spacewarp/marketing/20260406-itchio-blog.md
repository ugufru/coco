# Space Warp: A Game Forty Years in the Making

Before I owned a computer, I played one.

The computer club had two or three TRS-80 Model I machines, and we loaded them all from the same cassette. The game was called Space Warp, a real-time Star Trek battle simulation written by a sixteen-year-old named Josh Lavinsky, published by Personal Software in 1980. One kid flew the ship with the arrow keys. Another punched commands on the numeric keypad: warp coordinates, phaser angles, shield levels. It was cooperative by necessity and the effect was pure Star Trek. A helmsman and a commander, barking orders over a glowing phosphor screen.

I was twelve. It was my first computer game.

Eventually I got my own machine, a TRS-80 Color Computer, the CoCo. The Motorola 6809 inside it was one of the most elegant 8-bit CPUs ever designed. But the software never caught up to the silicon, and I never wrote the game I wanted to write for it.

Forty years and a career in software development later, I went back.

---

**Space Warp** is a from-scratch reimplementation of that original game, built for the TRS-80 Color Computer using a custom programming language I developed with Claude, Anthropic's AI coding assistant. The entire game runs in about 28 kilobytes. On a machine from 1980 with less memory than a single modern app icon.

The graphics use NTSC artifact coloring, a technique that coaxes four colors out of a monochrome framebuffer by exploiting how old televisions decode the signal. Your ship glows blue. Enemy Jovians burn red. Maser beams streak across a field of colored stars. It looks like nothing else because it *is* nothing else. Every pixel plotted by hand, every keystroke read directly from the hardware. No operating system. No libraries. No safety net.

**The Jovians have DNA.** Each enemy ship carries a four-byte genome that controls its aggression, pilot skill, speed, and appearance. Aggressive Jovians charge to point-blank range. Fearful ones orbit at distance and snipe. Skilled pilots thread through star fields while clumsy ones blunder into obstacles. They get angry when you kill their wingmates. They get scared when they're wounded. No two Jovians look or behave alike.

**Combat has weight.** Masers are your primary weapon: fast, direct, reliable. Missiles are the finisher: homing, devastating, limited. Your deflector shields absorb hits but degrade under sustained fire. Below 40%, damage bleeds through to your ship's systems. Engines, warp drive, scanners, weapons. When all five systems fail, you're done. Dock at a starbase to repair, but you'll have to lower your shields first. And the Jovians are attacking those bases too.

---

This game was built in 24 days by a 30-year software veteran and an AI that never sleeps. The development language, Bare Naked Forth, was written from scratch for this project. The cross-compiler, the kernel, the tutorial series, the emulator toolchain, the screen-capture debugging system: all built as we went, each tool earning its place.

The source code will be on GitHub. The game is free. If you want to know what it felt like to write software in 1982 with 2026 tools, the whole story is in the commit history.

Josh Lavinsky fit the original into 4,096 bytes of Z-80 assembly when he was sixteen years old. I still don't know how he did it. I needed seven times the memory and an AI partner to get here. But I got here.

**Space Warp releases April 15, 2026.** Play it in your browser. No emulator, no ROMs, no setup. A TRS-80 Color Computer running inside WebAssembly, Forth all the way down.

---

*Built with [Bare Naked Forth](https://github.com/pncunningham/coco) and [Claude Code](https://claude.ai/code).*
