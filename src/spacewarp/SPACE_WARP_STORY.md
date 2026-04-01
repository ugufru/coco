# Twenty-Four Bytes to Spare

*The story of Space Warp for the TRS-80 Color Computer — a game forty years in the making.*

---

Before Paul Cunningham owned a computer, he played one.

The computer club had two or three TRS-80 Model I machines, and the kids loaded them all from the same cassette. The game was called Space Warp — a real-time Star Trek battle simulation written by a teenager named Josh Lavinsky, published by Personal Software in 1980. One player took the arrow keys and flew the ship. Another worked the numeric keypad, punching in commands: warp coordinates, phaser angles, shield levels. It was cooperative by necessity — the keyboard couldn't do both jobs at once — and the effect was pure Star Trek. A helmsman and a commander, barking orders at each other over a glowing phosphor screen. Realistic starship combat roles, running in 16 kilobytes of RAM.

It was Paul's first computer game. He was twelve years old.

Eventually he got his own machine — a TRS-80 Color Computer, the CoCo, with its Motorola 6809 processor and its cramped BASIC prompt. The CoCo was capable hardware wrapped in frustrating software. Games like Project Nebula scratched the space-combat itch, but nothing quite matched what Space Warp had been. The 6809 was one of the most elegant 8-bit CPUs ever designed — two stack pointers, a real multiply instruction, addressing modes for every occasion — and yet the software ecosystem never caught up to the silicon. The machine deserved better tools than it got.

That thought lodged somewhere and stayed.

---

Thirty years of professional software development followed. Java, mostly — three decades of enterprise architecture, frameworks, libraries, dependency trees measured in gigabytes. Paul built a career writing code. The CoCo gathered dust, then nostalgia, then a kind of gravitational pull that never fully let go.

In early 2026, he decided to go back.

The original question was simple: what if the CoCo had shipped with real developer tools? Not BASIC, not the clunky EDTASM assembler, but a proper development environment — the toolchain the 6809 deserved in 1982 but never got. Paul started exploring the idea with Claude, Anthropic's AI coding assistant, and almost immediately the conversation took a turn he hadn't expected.

Claude suggested Forth.

It wasn't on Paul's radar as a modern development language for the 6809. But the more he thought about it, the more natural the fit became. Forth's dual-stack architecture mapped directly onto the 6809's two stack pointers. Hardcoded word addresses kept the threading tight. The result would be something close to assembly in performance while staying modular and comprehensible at the source level. A microkernel — just the inner interpreter and a handful of primitives — could live in its own memory region, separately optimized, while applications compiled to threaded bytecode that the kernel executed at native speed.

The first kernel was 106 bytes of 6809 assembly. Five primitives: DOCOL, EXIT, LIT, EMIT, HALT. Enough to print "HELLO, WORLD!" to the screen of an emulated CoCo. The cross-compiler, fc.py, read the kernel's symbol map and generated DECB binaries that could load and run on real hardware. The next commit's README called it "the wave begins here."

---

What followed was a partnership unlike anything Paul had experienced in thirty years of development.

Claude set up the entire toolchain — lwasm for cross-assembly, XRoar for emulation, the build pipeline connecting them. When the kernel crashed, Claude diagnosed addressing mode bugs that would have cost days of manual debugging. When Paul needed automated testing, Claude wrote a screen-capture MCP server that could photograph the emulator window and analyze what was on screen — debugging the game visually without Paul having to watch every run. When features needed specification, Claude generated detailed design documents and maintained an issue tracker that kept the project coherent from one session to the next.

Claude made mistakes, the way a junior developer makes mistakes — occasionally blind to alternative approaches, sometimes needing to be steered away from overengineered solutions. But the persistence was extraordinary. The energy and interest were always there, waiting for the next part. Paul brought the role of PM and seasoned architect: seeing the forest when Claude was deep in the trees, knowing when to refactor and when to ship, recognizing optimization opportunities that the AI couldn't see from inside the code. It was the dynamic of a senior engineer working with a tireless, eager junior — except the junior never slept, never lost context between sessions, and could search the entire CoCo technical reference in seconds.

The foundation went up fast. A thirteen-chapter tutorial series — *Getting Started with Bare Naked Forth* — grew alongside the kernel, each chapter paired with new primitives. Chapter 1 taught the stack. Chapter 6 added loops. Chapter 12 built a complete guessing game. Demo programs proved the platform could handle real applications: a Tetris clone in SG4 semigraphics, a kaleidoscope pattern generator, an RPN calculator with pixel-font digits.

Then came the trick that would make everything else possible. The kernel's bootstrap sequence paged out the CoCo's BASIC ROMs entirely — writing a single byte to the SAM register at $FFDF — freeing 32 kilobytes of RAM that normally held firmware the game would never use. The kernel loaded itself from video memory into the space where the ROM cartridge normally lived. It was a technique specifically designed to test the path toward a future Bare Naked Forth ROM cartridge, but its immediate effect was doubling the available memory for applications.

Two weeks in, the platform was ready for something ambitious.

---

Paul chose to build the game he'd been carrying since he was twelve.

Josh Lavinsky had written the original Space Warp in 1978, fitting a complete real-time Star Trek simulation into 4,096 bytes of Z-80 assembly. He was sixteen years old. He sold cassette tapes from his family's dining room before Personal Software — the company that would become VisiCorp, the people behind VisiCalc — licensed the game and put it in Radio Shack stores. Lavinsky's design document, with its hand-drawn flowcharts and memory allocation tables, survived on yellowed printouts for nearly fifty years.

Paul's reimplementation would be a different beast. Where Lavinsky used ASCII characters on a text-mode screen, Paul rendered bitmap sprites through the CoCo's NTSC artifact coloring — a technique that coaxed four colors out of a monochrome framebuffer by exploiting the way television sets decoded the signal. Where the original ran a simple AI loop (stall, repair, fire, maybe retreat), Paul designed a genome system.

The idea came from biology, and from two decades of thinking about emergent behavior. Paul had been simulating cellular automata since writing single-cell life forms in Java back in 1999. The principle was the same here: encode a small amount of DNA, let the environment do the rest. Each Jovian enemy ship carried a four-byte genome — aggression, initiative, pilot skill, speed, handedness, path style — and a real-time emotion state that modulated everything. A rage-filled Jovian engaged at twenty pixels and fired fast. A terrified one kept sixty-five pixels of distance and ran. Their appearances were procedurally generated from the same genome seed, so no two Jovians looked alike. The spectacle of uncertainty was never lost.

The AI system grew through six phases over three days: data structures, per-ship tick frequencies, emotion with stimulus and decay, detection and awareness states, quadrant mood persistence, and finally algorithmic sprite generation. Tick frequency *was* intelligence — a fast ace evaluating the world every two frames literally thought faster than a dolt thinking every eight. The system naturally spent CPU cycles where gameplay was hottest.

Meanwhile, memory was tightening. The game's data structures — galaxy maps, position arrays, sprite buffers, genome tables, a 64-byte mood grid tracking emotional state per quadrant — kept pushing against the boundaries. VRAM sat in the middle of the address space like a boulder in a river. Data had to be relocated from the application region to the all-RAM area above $8000. Mindmap-oriented module graphs helped Paul and Claude visualize where the bytes were going and where optimizations would yield the most benefit. A kernel primitive consolidation saved 262 bytes. Dead code removal saved another 256. Hand-written 6809 assembly CODE words replaced Forth in the hottest loops — sprite rendering, AI computation, collision detection — running ten to twelve times faster than threaded code.

The final binary came in at 24,552 bytes of compiled Forth, plus the 2.2-kilobyte kernel. Twenty-four bytes of headroom remained between the application code and the data region. Those twenty-four bytes were eventually used to fix a crash. There is no room left.

All of this — 163 commits, 214 tracked issues, a 59-primitive kernel, a 13-chapter tutorial, and a 3,855-line game — happened in twenty-four days.

---

Then things got meta.

Paul wanted people to play Space Warp without installing anything — no emulator, no ROM images, no command line. The answer was barenakedgames.com, a static website that runs the game entirely in the browser. The technology stack is a nesting doll of computing history: Forth source code, cross-compiled by a Python tool into 6809 bytecode, loaded into XRoar — a cycle-accurate CoCo emulator written in C — which has been compiled to WebAssembly via Emscripten — running inside JavaScript on a modern browser. The game's Forth programs even communicate with the browser through an IPC protocol: the CoCo writes an 8-byte signature at address $7F00, and JavaScript scans the WASM heap to find CoCo RAM and poll for navigation commands. A 1982 machine talking to a 2026 web browser through shared virtual memory.

The site opens with an animated home screen — "BARE NAKED GAMES" rendered in Lissajous curves with five bouncing stars simulating gravity and damping — itself a Forth program running on the virtual 6809. Press S and you're playing Space Warp. No installation, no configuration, no explanation needed. Forth all the way down.

---

Paul's son has been watching the development. He's started playtesting.

Paul doesn't think of Space Warp as a retro game. To him, it's his first real video game — written for the platform he always aspired to write one for, made possible by a toolchain that didn't exist until he and an AI built it together. After forty years, Bare Naked Forth was the epiphany that brought it all home.

After thirty years of Java development, Paul says he's no longer interested in writing code. He wants to build applications. The distinction matters. Claude makes it possible to generate large amounts of bespoke code without third-party dependencies — need a tool, and Claude builds it on the spot. The developer's role shifts from typist to architect, from writing lines to making decisions. The industry has become desensitized to the size of its deliverables; here, working within 28 kilobytes, every byte is earned and every decision is visible.

Space Warp releases April 15th, 2026, on itch.io. Free. The source code will be on GitHub. A complete real-time space combat game with emergent AI and procedural enemies, running on a forty-six-year-old machine that sold for under two hundred dollars — written by a twelve-year-old's dream, a thirty-year veteran's discipline, and an AI that never gets tired.

---

## Quick Round with Paul Cunningham

*Interview conducted by Claude (Anthropic) after exploring 163 commits, 214 issues, and 3,855 lines of Space Warp source code across twenty-four days of collaborative development.*

**You played the original Space Warp before you owned your own computer. What do you remember about that first time?**

We only had a few computers to play on. Two or three. We loaded them up from the same cassette. The game focuses on piloting with the arrow keys, but navigation and everything else was done on the numeric keypad. This is the first cooperative game I ever played in that context — one person at the helm, another making the commands. Realistic Star Trek style roles in 16K of RAM.

**What made you think Forth was the right language for this?**

Forth was not on my radar for a modern 6809 development language, but it was on the tip of Claude's tongue from the outset. It is a natural fit, and hardcoding the word addresses and the dual stacks make it about as close to assembler as I can get. This has yielded very tight code while keeping the overall codebase somewhat comprehensive and modular. The microkernel is key to quick wins in performance. We don't use the BASIC ROMs at all — they are paged out of memory to gain an extra 32K of RAM when the kernel bootstraps and loads itself out of video memory into where the ROM cartridge usually goes. That technique is specifically being tested to implement a Bare Naked Forth ROM cartridge in the future.

**The Jovian AI uses a 4-byte genome. Where did that idea come from?**

Biology is diverse. Our environments are diverse. Our looks and feelings. They all add up to influence our everyday actions. Aggression, wonder, fear — they all play into how a battle transpires in Space Warp. Cellular automata has been a specialty of mine since simulating single-cell life forms in Java back in 1999. The spectacle of uncertainty is never lost in a game when generative behaviors and appearances are supported. We did what we could in 28K. I have no idea how this could have been done using a Z-80 and 4K. That remains a technical wonder.

**What would 12-year-old you think of this game?**

My son has been watching me develop it, and he's started playtesting it. I don't know what young people think about "retro" games, but I see it more as my first real video game on a platform I had always aspired to write one for. Bare Naked Forth was the epiphany that made it all come together after 40 years.

**You've described Claude as helping you break down technical barriers. Can you give a specific example?**

So much of this I've known how to do for years. All the details of the CoCo and its limitations, tips and tricks. Between the cross compilers, Python, the vast knowledge archives about the CoCo, and XRoar, getting this rolling still would not have happened without the persistence of Claude Code running on a Macintosh. All the tools just flew in — Claude set them up, got them running, and helped with each step along the way. There were many crashes and freezes it was able to quickly diagnose, that would have kept me looking for days. The "what if" moments, like writing a native screen-capture MCP so the game could be tested and debugged at the same time without my assistance. The invaluable specs, documentation, and issue tracking that has kept the project on track and easily understood and changed each new session. The ability to do this while simultaneously searching for a new career path, doing other MIDI-based projects on embedded systems, website development, and even gathering up tax receipts — all kept me on target from day to day. It is a joy to be working with AI as a development partner. Claude makes mistakes, just like a junior developer might, but it is relentless in its pursuit of solving the problem at hand. Often it takes the experience of a PM and a seasoned engineer to get it back on track. It's no different with real developers, but the energy and interest is always there waiting for the next part. That's nice to have in a development partner.

**The game runs in 24,552 bytes with 24 bytes to spare. Was there a moment where you thought you'd run out of room?**

We used those 24 bytes to fix a crash. It's getting really tight. I don't think we can add more, but there are always questions to ask Claude about how we can do better. There are always new ways to view the problem, solutions to be consolidated in the microkernel, dead and complicated code that can be removed and simplified. As developers we have become desensitized to the size of our application deliverables and their dependencies. It has become a huge security liability in the industry. Claude makes it possible to write large amounts of bespoke code, limiting the need for third-party libraries and drivers. Need a special tool? Claude can make it for you right there. It does its own work this way, if you watch close — writing inline Python to generate files and other operations like that. After 30 years of Java development, I'm no longer interested in writing code. Let's get to building those applications.

**Josh Lavinsky fit the original into 4K of Z-80. You're at ~28K of Forth on a 6809. How do you think about that comparison?**

He was amazing. I wish I could begin to see how it happened. I suppose we could have disassembled it and found out, but I kind of prefer the mystery and magic. It really was magic, when I was 12, to roleplay starship combat. It was still magic when I finally found it to play on a TRS-80 emulator, and gave me pause to wonder how I was still not finished with 28K. Bitmap graphics versus ASCII video maps are a lot more work, but the effect is very retro and fun to watch for a CoCo-based game.

**What's next?**

Finish up and balance the Space Warp gameplay through playtesting. The release date will be April 15th on itch.io for free, and on GitHub.

---

*Space Warp for the TRS-80 Color Computer. Built with [Bare Naked Forth](forth/kernel/) and [Claude Code](https://claude.ai/code). Releasing April 15, 2026.*
