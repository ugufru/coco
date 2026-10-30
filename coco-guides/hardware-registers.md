# CoCo Hardware Registers ($FF00-$FFFF)

Every hardware register in the CoCo 1, 2 and 3 I/O page, by address. Bit
layouts, read/write behaviour, which models have it, and where this
project already uses it.

**Sources.** The address map and bit layouts come from Chris Lomont,
*Color Computer 1/2/3 Hardware Programming*, v0.82 (Aug 2007), pp. 48-73:
<https://www.lomont.org/software/misc/coco/Lomont_CoCoHardware.pdf>.
Lomont asks that the PDF not be reposted, so it is not in the repo; a
local copy (gitignored) lives at `coco-guides/lomont/`. Everything here is
rewritten in our own words and checked against the Tandy *Color Computer
Technical Reference Manual* (`coco-guides/coco_technical_reference.pdf`,
memory map pages). Where the two disagree, or Lomont has a slip, it is
called out in [Corrections to the source](#corrections-to-the-source).
Cartridge-chip entries that the project has measured or driven itself
cite the library file instead.

**Conventions.** `$FFxx` is a 6809 CPU address. Models: **1/2/3** means
all three, **3** means CoCo 3 only. R = read, W = write. "Used by" names
the project file that touches the register, so you can find a working
example.

Related guides: `vdg-modes.md` (VDG/SAM display modes in depth),
`display-modes.md` (VDG vs GIME), `coco3-intro.md` (GIME architecture).

---

## Map at a glance

| Range         | Device                                   | Models | Notes |
|---------------|------------------------------------------|--------|-------|
| `$FF00-$FF03` | PIA0 (MC6821): keyboard, joystick, HS/FS IRQ | 1/2/3 | Mirrored through `$FF1F` |
| `$FF20-$FF23` | PIA1 (MC6821): DAC, cassette, VDG mode, sound, cart FIRQ | 1/2/3 | Mirrored through `$FF3F` |
| `$FF40-$FF5F` | SCS: cartridge I/O (disk controller, Becker port, sound carts) | 1/2/3 | Selected by Multi-Pak `$FF7F` bits 1-0 |
| `$FF60-$FF7F` | Other cartridge I/O (serial paks, sound, speech, Multi-Pak) | 1/2/3 | Not slot-switched |
| `$FF80-$FF8F` | Unused (old SuperVoice at `$FF80-$FF84`) | 1/2/3 | |
| `$FF90-$FFBF` | GIME: init, IRQ/FIRQ, timer, video, MMU, palette | 3 | Unused on CoCo 1/2 |
| `$FFC0-$FFDF` | SAM (MC6883) control bits, write-only | 1/2/3 | Even address clears, odd sets |
| `$FFE0-$FFF1` | Reserved | 1/2/3 | |
| `$FFF2-$FFFF` | 6809 interrupt and reset vectors | 1/2/3 | Read from ROM (`$BFF2-$BFFF` on 1/2) |

---

## PIA basics (applies to PIA0 and PIA1)

Each MC6821 PIA has two sides, A and B, and each side has two addresses:

- **Even address**: the data register *or* the data direction register
  (DDR), depending on bit 2 of the control register.
- **Odd address**: the control register.

Control register layout (same for all four):

| Bit | Meaning |
|-----|---------|
| 7   | Interrupt flag for the CA1/CB1 input (R only). Cleared by reading the data register. |
| 6   | Flag for CA2/CB2 as input (not used on the CoCo) |
| 5-3 | CA2/CB2 control. On the CoCo bits 5-4 are always `1 1` (CA2/CB2 is an output, set by bit 3), so **bit 3 is a plain output line** |
| 2   | 0 = even address is the DDR, 1 = even address is the data register (normal) |
| 1   | Interrupt edge: 0 = falling, 1 = rising |
| 0   | Interrupt enable: 0 = off, 1 = pass to CPU |

In the DDR, a 1 makes that bit an output, a 0 an input. Normally the DDR
is touched only at initialization: clear bit 2, write the DDR, set bit 2.

The PIAs only decode the low two address bits, so PIA0 repeats every 4
bytes through `$FF1F` and PIA1 through `$FF3F` (`$FF1E` is `$FF02`).

---

## PIA0: `$FF00-$FF03` (keyboard, joystick, video sync IRQs)

### `$FF00` PIA0 A data: keyboard rows, joystick buttons, comparator (R) (1/2/3)

| Bit | Meaning |
|-----|---------|
| 7   | Joystick comparator output (DAC level vs selected axis) |
| 6   | Keyboard row 7 |
| 5   | Keyboard row 6 |
| 4   | Keyboard row 5 |
| 3   | Keyboard row 4, left joystick button 2 (CoCo 3) |
| 2   | Keyboard row 3, right joystick button 2 (CoCo 3) |
| 1   | Keyboard row 2, left joystick button 1 |
| 0   | Keyboard row 1, right joystick button 1 |

A pressed key reads 0. Scan by writing one 0 bit to `$FF02` (column) and
reading `$FF00` (rows). The project's key table is in `kernel/kernel.asm`
(`KEY_TABLE`); `lib/keyboard.fs` groups keys wrongly, see CLAUDE.md.

Used by: `kernel/kernel.asm` (KEY), `lib/keyboard.fs`, `lib/dac.fs`,
`src/bounce/bounce.fs`.

### `$FF01` PIA0 A control: HSYNC IRQ, MUX select low (1/2/3)

| Bit | Meaning |
|-----|---------|
| 7   | HSYNC flag (63.5 us line rate) |
| 3   | Analog MUX select, LSB (SEL1) |
| 2   | DDR select for `$FF00` |
| 1   | HSYNC edge |
| 0   | HSYNC IRQ enable |

### `$FF02` PIA0 B data: keyboard columns (W) (1/2/3)

Bits 7-0 = keyboard columns 8-1. Write a 0 to the column(s) to scan.
Write `$FF` to release all columns (needed before reading joystick
buttons cleanly). Bit 6 doubles as a RAM size output on early boards.

### `$FF03` PIA0 B control: VSYNC (field sync) IRQ, MUX select high (1/2/3)

| Bit | Meaning |
|-----|---------|
| 7   | VSYNC flag (60 Hz field rate, 16.667 ms) |
| 3   | Analog MUX select, MSB (SEL2) |
| 2   | DDR select for `$FF02` |
| 1   | VSYNC edge |
| 0   | VSYNC IRQ enable |

The two MUX select bits pick the joystick axis for the comparator, and
also route the sound source when `$FF23` bit 3 enables sound:

| `$FF03` b3 | `$FF01` b3 | Joystick input | Sound source |
|-----------|-----------|----------------|--------------|
| 0 | 0 | Right X | 6-bit DAC |
| 0 | 1 | Right Y | Cassette input |
| 1 | 0 | Left X  | Cartridge sound |
| 1 | 1 | Left Y  | None |

Reading `$FF02` clears the VSYNC flag; IRQ handlers must do it.
Used by: `kernel/kernel.asm` (VSYNC wait, `$FF03` bit 7),
`src/terminal/terminal.fs` (cursor blink), `lib/sound.fs` (MUX select).

---

## PIA1: `$FF20-$FF23` (DAC, cassette, VDG, sound)

### `$FF20` PIA1 A data: 6-bit DAC, cassette in, RS-232 out (1/2/3)

| Bit | Meaning |
|-----|---------|
| 7-2 | 6-bit DAC (bit 7 MSB). Sound output and joystick reference |
| 1   | RS-232 (bit-banger) data output |
| 0   | Cassette data input |

Used by: `lib/dac.fs`, `lib/sound.fs`, `lib/async-sound.fs`,
`src/sound-tour/sound-tour.fs`.

### `$FF21` PIA1 A control: CD FIRQ, cassette motor (1/2/3)

| Bit | Meaning |
|-----|---------|
| 7   | CD (RS-232 carrier detect) FIRQ flag |
| 3   | Cassette motor relay: 0 = off, 1 = on |
| 2   | DDR select for `$FF20` |
| 1   | CD edge |
| 0   | CD FIRQ enable |

### `$FF22` PIA1 B data: VDG mode, single-bit sound, RAM size (1/2/3)

| Bit | Dir | Meaning |
|-----|-----|---------|
| 7   | out | VDG A/G: 0 = alphanumeric/semigraphics, 1 = full graphics |
| 6   | out | VDG GM2 |
| 5   | out | VDG GM1 (also INV on the VDG text path) |
| 4   | out | VDG GM0 and INT/EXT (SG4 vs SG6 on a plain 6847) |
| 3   | out | VDG CSS (colour set select) |
| 2   | in  | RAM size input (low = 4K chips, high = 16K) |
| 1   | out | Single-bit sound output |
| 0   | in  | RS-232 (bit-banger) data input |

On the CoCo 2B's **MC6847T1** bit 4 selects lowercase instead, and the T1
has no SG6 (#573). Set the SAM V bits (`$FFC0-$FFC5`) to match the mode;
see `vdg-modes.md`.

Used by: `lib/vdg.fs`, `lib/console.fs` (`lower-on`), most graphics demos.

### `$FF23` PIA1 B control: cartridge FIRQ, sound enable (1/2/3)

| Bit | Meaning |
|-----|---------|
| 7   | Cartridge (CART*) FIRQ flag |
| 3   | Sound enable (analog MUX output to the TV) |
| 2   | DDR select for `$FF22` |
| 1   | CART* edge |
| 0   | CART* FIRQ enable (autostart carts use this) |

Used by: `lib/sound.fs`.

---

## SCS cartridge I/O: `$FF40-$FF5F`

This range is decoded by the cartridge (the SCS line). With a Multi-Pak,
`$FF7F` bits 1-0 pick which slot sees it. Several of the devices below
claim the same addresses, so only one can be fitted at a time.

### Floppy disk controller (Tandy FD-50x, WD179x, 1/2/3)

| Address | R | W | Notes |
|---------|---|---|-------|
| `$FF40` | | DSKREG (control latch) | Write-only |
| `$FF41-$FF47` | | DSKREG images | Partial decode copies of `$FF40` |
| `$FF48` | Status | Command | |
| `$FF49` | Track | Track | Use SEEK rather than writing directly |
| `$FF4A` | Sector | Sector | 1-18; may be written directly |
| `$FF4B` | Data | Data | Must be serviced at disk rate or data is lost |

`$FF40` DSKREG:

| Bit | Meaning |
|-----|---------|
| 7   | HALT enable: data requests halt the CPU until the FDC is ready |
| 6   | Drive select 3 (also side select on double-sided drives) |
| 5   | Density: 0 = single, 1 = double |
| 4   | Write precompensation (use on inner tracks, above 22) |
| 3   | Drive motor on |
| 2-0 | Drive select 2, 1, 0 |

Common commands written to `$FF48`: RESTORE `$03`, SEEK `$17`, STEP `$23`,
STEP IN `$43`, STEP OUT `$53`, READ SECTOR `$80`, WRITE SECTOR `$A0`,
READ ADDRESS `$C0`, READ TRACK `$E4`, WRITE TRACK `$F4`, FORCE INTERRUPT
`$D0`. The low bits of type I commands set the step rate (00 = 6 ms,
01 = 12, 10 = 20, 11 = 30). Status bits read back from `$FF48`:

| Bit | Meaning (varies by command type) |
|-----|---------|
| 7 | Not ready |
| 6 | Write protect |
| 5 | Head loaded / record type |
| 4 | Seek error / record not found |
| 3 | CRC error |
| 2 | Track 0 / lost data |
| 1 | Index / data request |
| 0 | Busy |

Used by: `lib/decb.fs` (sector reads).

### Becker port (XRoar, DriveWire) (1/2/3, emulator and FPGA only)

| Address | Meaning |
|---------|---------|
| `$FF41` | Status: bit 1 set = a received byte is waiting |
| `$FF42` | Data: read = next byte, write = send a byte |

Overlaps the DSKREG images and the GameMaster SN76489, so it cannot share
the bus with either. **Never peek `$FF40-$FF4F` blindly over the terminal
link**: reading the Becker data port consumes a byte and breaks the link.

Used by: `lib/becker.fs`, `src/terminal/terminal.fs`, `lib/vgm.fs`.

### GameMaster cartridge (SN76489) (1/2/3)

Any write to an odd address in `$FF40-$FF5F` reaches the SN76489; the
project writes `$FF41`. `$FF5E` selects its ROM bank. Clashes with the
Becker port and the CoCo PSG.

Used by: `lib/sn76489.fs`, `src/jukebox/jukebox.fs`.

### CoCo PSG (YM2149) (1/2/3)

| Address | Meaning |
|---------|---------|
| `$FF5D` | Bit 0 = clock: 0 = 2 MHz, 1 = 1 MHz |
| `$FF5E` | Register select (latch) |
| `$FF5F` | Register data (R/W) |

Probe for it before writing (`ay-found?`), because a GameMaster in the
same space turns these writes into SN76489 noise.

Used by: `lib/ay8910.fs`.

### Other SCS users

These decode inside the SCS window, so with a Multi-Pak they answer only
in the slot `$FF7F` bits 1-0 select. Source tags as in the next section.

| Address | Device | Notes | Src |
|---------|--------|-------|-----|
| `$FF40-$FF42` | Disto RAM cart (256K-1M) | 24-bit address: `$FF40` low, `$FF41` mid, `$FF42` high byte. R/W | M |
| `$FF43` | Disto RAM cart data | Reads or writes the byte at that address. No auto-increment | M |
| `$FF50-$FF57` | Disto mini expansion bus (MEB) | On the Disto RAM cart and Super Controller; carries the Disto clock and printer adapters | M, D |
| `$FF50-$FF58` | Glenside IDE (jumper J2 = `$FF5x`) | Layout as in the IDE entry below | M |
| `$FF40-$FF5F` | X-SID (SID chip) | SID registers 0-31 straight from the SCS offset | M |
| `$FF40-$FF47` | Terco T4426 CNC trainer: 6821 PIA | Repeats every 4 bytes. Port A banks the cart ROM (one active-low bit per 8K bank) | M |
| `$FF4A-$FF4B`, `$FF4E-$FF4F` | Terco T4426: 6551 ACIA | Decoded only with A1 = 1; the ACIA register is A0 | M |

---

## Other cartridge I/O: `$FF60-$FF7F` and beyond

The Multi-Pak routes only `$FF40-$FF5F` (SCS) and the cart ROM (CTS) to
one slot. Everything here reaches every slot at once, and each cart
decodes its own addresses. Two carts that overlap cannot be fitted
together, even in different Multi-Pak slots. A few carts sit outside
`$FF60-$FF7F` entirely; they are collected at the end of this section.

**Source tags** (the Src column):

- **M**: MAME's CoCo cartridge emulation, `src/devices/bus/coco/`
  (<https://github.com/mamedev/mame/tree/master/src/devices/bus/coco>).
  Address decode read from the source of each device.
- **T**: Tim Lindner, *CoCo Memory Map Reference*
  (<https://tlindner.macmess.org/?page_id=767>). Lindner wrote several of
  the MAME CoCo devices.
- **L**: Lomont v0.82 (see the top of this page).
- **E**: R. Allen Murphey, exileinparadise.com pages on the Speech/Sound
  Cartridge and CoCoIO.
- **D**: Disto manuals on colorcomputerarchive.com (Super Controller II,
  Super Controller), as quoted in search results. Not read in full.

An entry tagged **L** alone is unconfirmed: nobody else we found
documents it.

### Map

| Address | Device | Notes | Src |
|---------|--------|-------|-----|
| `$FF60-$FF62` | X-Pad graphics tablet | X (0-255), Y (0-191), status. Reading X latches Y and status | L, T |
| `$FF60-$FF63` | Symphony-12 (Speech Systems, 4x AY-3-8910) | 6821 PIA. See below | M |
| `$FF60-$FF67` | CoCo Max A/D module | Address disputed, see [Conflicts](#conflicts-between-sources) | L |
| `$FF60-$FF7F` | TC^3 SCSI (Cloud-9) | Two addresses somewhere in this range | L |
| `$FF68-$FF6B` | Deluxe RS-232 Program Pak (6551 ACIA) | Data, status, command, control | M, T, L |
| `$FF68-$FF6B` | CoCoIO network: W5100S (base `$FF60`) | Jumper moves it to `$FF78-$FF7B`. See below | E |
| `$FF6C-$FF6F` | Direct Connect Modem Pak (6551 ACIA) | Same layout as the RS-232 Pak | M, T, L |
| `$FF6E-$FF6F` | MIDI Pak (Rutherford Research), MIDI Maestro (Go4Retro) | MC6850 ACIA. Overlaps the Modem Pak | M |
| `$FF70` | LR-Tech SASI (alternate address) | Default at `$FF74` | L |
| `$FF70`, `$FF72` | Musica stereo pack | Two DACs. Looks like a PIA at `$FF70-$FF73` (A and B data) | L |
| `$FF70-$FF72` | Laser light show D/A | X, Y, Z (intensity) | L |
| `$FF70-$FF73` | Stereo Composer (Speech Systems) | 6821 PIA, one 8-bit DAC per port. No ROM | M |
| `$FF70-$FF74` | SuperVoice (new model), CyberVoice (Burke & Burke) | Votrax SC-02. Layout not documented | L |
| `$FF70-$FF78` | Glenside IDE (jumper J2 = `$FF7x`) | See below | M, L |
| `$FF74` | LR-Tech SASI (default) | Layout not documented | L |
| `$FF74-$FF77` | Disto Super Controller II | Control and data registers of the haltless controller | D, L |
| `$FF76-$FF77`, `$FF79` | Word-Pak RS (PBJ, 80 columns) | R6545 CRTC, as Word-Pak below at the RS addresses | M |
| `$FF78-$FF7B` | WordPak 2+ (V9958 video) | See below | M |
| `$FF78-$FF7B` | CoCoIO network: W5100S (base `$FF70`) | | E |
| `$FF7A` | Orchestra-90 left DAC | 8-bit, write-only | M, T, L |
| `$FF7B` | Orchestra-90 right DAC | 8-bit, write-only | M, T, L |
| `$FF7C` | Unused | | L |
| `$FF7D` | Speech/Sound Cartridge: reset | See below | M, T, E |
| `$FF7E` | Speech/Sound Cartridge: data / status | See below | M, T, E |
| `$FF7F` | Multi-Pak slot select | See below | M, T, L |
| `$FF80-$FF84` | SuperVoice (old model) | Votrax SC-02. Layout not documented | L |
| `$FF90-$FF97` | CoCo Max hi-res input module | MAME's address; clashes with the GIME on a CoCo 3 | M |
| `$FF98-$FF99`, `$FF9B` | Word-Pak, Word-Pak II (PBJ, 80 columns) | R6545 CRTC. Clashes with the GIME on a CoCo 3 | M |
| `$FF9C` | Word-Pak II video switch | Bit 6 switches the display between the CoCo and the Word-Pak | M |

### Speech/Sound Cartridge (Tandy 26-3144) (1/2/3)

A TMS7040 microcontroller runs the cart. Behind it sit an SP0256-AL2
speech chip, an AY-3-8913 PSG (clocked at about 1.79 MHz), and 2K of
RAM. The CoCo never touches those chips directly: it writes command
bytes to the microcontroller one at a time.

| Address | R | W |
|---------|---|---|
| `$FF7D` | `$FF` | Reset. Bit 0 = 1 holds the speech chip in reset; taking bit 0 from 1 to 0 resets the microcontroller and PSG |
| `$FF7E` | Status | Next byte for the microcontroller (raises its interrupt and sets busy) |

`$FF7E` status (each flag is active low):

| Bit | Meaning |
|-----|---------|
| 7 | 1 = ready for the next byte, 0 = busy with the last one |
| 6 | 0 = speaking |
| 5 | 0 = sound playing |
| 4-0 | Read as 1 |

To reset, write 1 to `$FF7D`, wait, write 0. Before each byte, wait for
`$FF7E` bit 7 to be 1. Sending `$AF` enters direct PSG mode, after
which bytes go in pairs: a PSG register number, then its value. `$CF`
stops all sound. Text-to-speech and allophone commands are in Tandy's
manual, which we have not read. To hear it, route the analog MUX to the
cartridge (`$FF03` bit 3 = 1, `$FF01` bit 3 = 0) and set `$FF23` bit 3.

Sources: MAME `coco_ssc.cpp` (register behaviour), Lindner (status
bits), exileinparadise.com (procedure, `$AF` and `$CF`).

### 6551 ACIA (RS-232 Pak `$FF68`, Modem Pak `$FF6C`) (1/2/3)

| Offset | R | W |
|--------|---|---|
| +0 | Receive data | Transmit data |
| +1 | Status | Programmed reset |
| +2 | Command | Command |
| +3 | Control | Control |

Status: b7 IRQ, b6 /DSR, b5 /DCD, b4 transmit empty, b3 receive full,
b2 overrun, b1 framing error, b0 parity error. Control: b7 stop bits
(0 = 1), b6-5 word length (00 = 8 ... 11 = 5), b4 internal baud clock,
b3-0 baud rate (`$6` = 300, `$8` = 1200, `$A` = 2400, `$C` = 4800,
`$E` = 9600, `$F` = 19200). Command: b7-5 parity, b4 echo, b3-2
transmitter / RTS, b1 receive IRQ disable, b0 DTR.

Both paks also carry a ROM on CTS (4K in the RS-232 Pak, 8K in the
Modem Pak).

### MC6850 ACIA (MIDI Pak `$FF6E`) (1/2/3)

| Address | R | W |
|---------|---|---|
| `$FF6E` | Status | Control |
| `$FF6F` | Receive data | Transmit data |

Standard 6850 layout (from the 6850 datasheet, not checked against a pak
manual). Status: b0 receive full, b1 transmit empty, b7 IRQ. On the
Dragon the same pak sits at `$FF74-$FF75` (MAME).

### Symphony-12 (Speech Systems) (1/2/3)

A 6821 PIA at `$FF60-$FF63` drives four AY-3-8910s (twelve voices).
Port A (`$FF60`) is the shared data bus. Port B (`$FF62`) has two
control bits per chip: bits 1-0 chip 0, 3-2 chip 1, 5-4 chip 2, 7-6
chip 3. Per chip: `11` latch register number, `10` write data, `01`
read data, `00` idle. Chips 0 and 1 play left, 2 and 3 right. A jumper
picks stereo (RCA jacks) or mono (back through the cart port). No ROM.

Sources: MAME `coco_sym12.cpp`; `lib/ay8910.fs` for the clock
(14.31818 MHz / 16).

Used by: `lib/ay8910.fs`.

### Stereo Composer (Speech Systems) (1/2/3)

A 6821 PIA at `$FF70-$FF73` with an 8-bit DAC on each port: write
`$FF70` (port A) and `$FF72` (port B) once the DDRs are set to outputs.
No ROM. Which port is left is not stated in MAME. The Musica stereo
pack's addresses (`$FF70` and `$FF72`, Lomont) are exactly the two PIA
data registers, so it may be the same hardware; unconfirmed.

### Glenside IDE (base `$FF50` or `$FF70`) (1/2/3)

| Offset | R | W |
|--------|---|---|
| +0 | Data, low byte (latches the high byte into +8) | Data, low byte (sends +8 as the high byte) |
| +1 | Error | Features |
| +2 | Sector count | Sector count |
| +3 | Sector number | Sector number |
| +4 | Cylinder low | Cylinder low |
| +5 | Cylinder high | Cylinder high |
| +6 | Device/head | Device/head |
| +7 | Status | Command |
| +8 | Data high-byte latch | Data high-byte latch |

16-bit IDE data goes through the latch: on a read, read +0 first, then
+8; on a write, write +8 first, then +0. Jumper J2 picks the base. At
`$FF5x` it is inside SCS and follows the Multi-Pak slot; at `$FF7x` it
is not. ATAPI changes most of these registers and is not supported by
the stock driver (Lomont).

Sources: MAME `coco_ide.cpp`, Lomont.

### Word-Pak, Word-Pak II, Word-Pak RS (PBJ, 80 columns) (1/2)

An R6545 CRTC with its own video RAM, driving a separate monitor.

| Word-Pak / II | Word-Pak RS | R | W |
|---------------|-------------|---|---|
| `$FF98` | `$FF76` | CRTC status | CRTC register select |
| `$FF99` | `$FF77` | CRTC register | CRTC register |
| `$FF9B` | `$FF79` | | Byte to video RAM at the CRTC update address |
| `$FF9C` | | | Word-Pak II only: bit 6 = video switch |

The Word-Pak and Word-Pak II addresses overlap the GIME, so they are
CoCo 1/2 only. MAME does not say what the RS version is for; that it
exists for the CoCo 3 is our guess.

Source: MAME `coco_wpk.cpp`.

### WordPak 2+ (V9958) (1/2/3)

A Yamaha V9958 video chip at `$FF78-$FF7B`; its interrupt drives the
CPU's NMI. MAME passes the four addresses straight to the chip, so the
layout is the V9958's own: +0 VRAM data, +1 status / register setup,
+2 palette, +3 indirect register (from the V9938/V9958 datasheets, not
checked against a WordPak 2+ manual).

Source: MAME `coco_wpk2p.cpp`.

### CoCoIO network board (W5100S) (1/2/3)

A modern board with a WIZnet W5100S Ethernet chip. A jumper sets the
base to `$FF60` or `$FF70`, which puts the chip at `$FF68-$FF6B` or
`$FF78-$FF7B`. At `$FF68` it collides with the RS-232 Pak. Four
registers is the W5100S indirect bus mode: +0 mode, +1 address high,
+2 address low, +3 data. (The source page labels them command, internal
address and data port, and calls the chip a 16550 in one line; the
layout above follows the W5100S datasheet.)

Source: exileinparadise.com, CoCoIO page.

### X-Pad graphics tablet (1/2/3)

| Address | Meaning |
|---------|---------|
| `$FF60` | X, 0-255. Reading it latches Y and status |
| `$FF61` | Y, 0-191 |
| `$FF62` | Status, bits 3-0: pen down, pen within 1 inch, pen in X margin, pen in Y margin |

(0, 0) is the top left, and the coordinates wrap at the margins. The
status bit order is disputed, see below.

### CoCo Max input modules

The CoCo Max input module is an A/D converter, and reads are pipelined:
reading a channel's address starts that channel's conversion and returns
the result of the *previous* conversion. To read a channel, read its
address twice (or read the next channel's address).

MAME (`coco_max.cpp`, "CoCo Max Hi-Res Input Module") puts it at
`$FF90-$FF97`: channel 0 = Y, 1 = X, 2 = button 1, 3 = button 2
(`$00` while pressed, `$FF` when not), 4-7 not connected. Lomont puts
"the CoCo Max A/D module" at `$FF60-$FF67` with 0 = X, 1 = Y, 2 = pen
switch. These may be two different products, see below.

### `$FF7F` Multi-Pak slot select (1/2/3)

| Bits | Meaning |
|------|---------|
| 7-6 | Unused (read as 1) |
| 5-4 | CTS and CART* slot (cart ROM at `$C000-$DFFF`, and the cart FIRQ), 0-3 = slot 1-4 |
| 3-2 | Unused (read as 1) |
| 1-0 | SCS slot (I/O at `$FF40-$FF5F`), 0-3 = slot 1-4 |

Write `$00`, `$11`, `$22`, `$33` to select slot 1, 2, 3, 4 for both.
At reset both fields follow the front-panel switch (`$CC`, `$DD`, `$EE`,
`$FF` read back for slots 1-4). The first write locks out the switch
until the next reset, and from then on reads return what was written.

Source: MAME `coco_multi.cpp`, Lindner.

Used by: `src/jukebox/jukebox.fs` (disk in one slot, GameMaster in the other).

### `$FF38-$FF39` Deluxe CoCo AY-3-8913

Planned for the unreleased Deluxe CoCo. On a CoCo 1/2 these addresses
are PIA1 mirrors, so the project leaves the chip out (`lib/ay8910.fs`).

### Conflicts between sources

1. **CoCo Max address.** MAME: `$FF90-$FF97`, channel 0 = Y, 1 = X.
   Lomont: `$FF60-$FF67`, channel 0 = X, 1 = Y. MAME's name is the
   "Hi-Res Input Module". CoCo Max I/II and CoCo Max III shipped
   different hardware, so both may be right for different products; we
   have not found a manual to settle it. Note that `$FF90-$FF97` is
   GIME space on a CoCo 3.
2. **X-Pad status bits.** Lomont: bit 0 pen down, 1 within 1 inch, 2 X
   margin, 3 Y margin. Lindner: bit 3 pen down, 2 within 1 inch, 1 X
   margin, 0 Y margin. Unresolved.
3. **CoCoIO chip.** The CoCoIO page calls the `$FF68` device both a
   16550 UART and the W5100S. We follow the W5100S reading.

### Still undocumented

Register layouts for SuperVoice and CyberVoice (Votrax SC-02), TC^3
SCSI, LR-Tech SASI, the Disto SCII's `$FF74-$FF77` registers, the laser
light show D/A, and the Speech/Sound Cartridge's speech commands. The
likely sources are the original manuals on colorcomputerarchive.com
(Burke & Burke CoCo XT, Disto SCII, Tandy Speech/Sound Cartridge) and
NitrOS-9's driver source. Cocopedia's "External Hardware IO Address Map"
page looks like the most complete list, but it refused our fetches
(HTTP 403).

---

## GIME: `$FF90-$FFBF` (CoCo 3 only)

On a CoCo 1/2, `$FF80-$FFBF` is unused.

### `$FF90` INIT0 (3)

| Bit | Name | Meaning |
|-----|------|---------|
| 7   | COCO | 1 = CoCo 1/2 compatible video, 0 = CoCo 3 video modes |
| 6   | M/P  | 1 = MMU enabled |
| 5   | IEN  | 1 = GIME IRQ output to CPU enabled |
| 4   | FEN  | 1 = GIME FIRQ output to CPU enabled |
| 3   | MC3  | 1 = vector RAM at `$FExx` (constant page) enabled |
| 2   | MC2  | 1 = standard SCS (`$FF40-$FF5F` external) |
| 1-0 | MC1-0 | ROM map: `0x` = 16K internal + 16K external, `10` = 32K internal, `11` = 32K external (except vectors) |

RS-DOS uses `$C4` (196) for CoCo 2 modes and `$44` (68) for CoCo 3 modes.

### `$FF91` INIT1 (3)

| Bit | Meaning |
|-----|---------|
| 6   | Memory type: 1 = 256K chips, 0 = 64K chips |
| 5   | TINS, timer clock: 1 = 279.365 ns (3.58 MHz), 0 = 63.695 us (scan line) |
| 0   | MMU task: 0 = `$FFA0-$FFA7`, 1 = `$FFA8-$FFAF` |

### `$FF92` IRQENR / `$FF93` FIRQENR (3)

Same layout; one generates IRQ, the other FIRQ. Write to enable, **read to
see which sources fired and acknowledge them**. A bit also reads 1 while
its input pin is held low.

| Bit | Name  | Source |
|-----|-------|--------|
| 5   | TMR   | 12-bit timer reached zero |
| 4   | HBORD | Horizontal border (falling HSYNC) |
| 3   | VBORD | Vertical border (falling VSYNC) |
| 2   | EI2   | Serial port pin 4 falling edge |
| 1   | EI1   | Keyboard: a 0 on any of PIA0 PA0-PA6 |
| 0   | EI0   | Cartridge pin 8 (CART*) falling edge |

INIT0 bit 5 (IEN) or bit 4 (FEN) must also be set.

### `$FF94-$FF95` Timer (3)

`$FF94` bits 3-0 = timer bits 11-8, and **writing `$FF94` restarts the
count**. `$FF95` = bits 7-0, stored without restarting. 12-bit count
0-4095 at the TINS rate; on reaching zero it reloads and raises TMR. Zero
in both stops it. The 1986 GIME counts n+2, the 1987 GIME n+1. The timer
also paces blinking text.

### `$FF96-$FF97` Unused (3)

### `$FF98` VMODE (3)

| Bit | Name | Meaning |
|-----|------|---------|
| 7   | BP     | 0 = text, 1 = bitplane graphics |
| 5   | DESCEN | Text: true lowercase descenders. Graphics: swap artifact colours |
| 4   | MOCH   | 1 = monochrome composite output |
| 3   | H50    | 1 = 50 Hz, 0 = 60 Hz |
| 2-0 | LPR    | Lines per text row: 000 = 1, 001 = 2, 010 = 3, 011 = 8, 100 = 9, 101 = 10, 110 = 11, 111 = infinite |

### `$FF99` VRES (3)

| Bits | Meaning |
|------|---------|
| 6-5 | Lines per field: 00 = 192, 01 = 200 (shows 199), 10 = zero/infinite, 11 = 225 |
| 4-2 | Graphics bytes per row: 000 = 16, 001 = 20, 010 = 32, 011 = 40, 100 = 64, 101 = 80, 110 = 128, 111 = 160. Text: `0x0` = 32, `0x1` = 40, `1x0` = 64, `1x1` = 80 columns |
| 1-0 | Graphics colours: 00 = 2, 01 = 4, 10 = 16. Text: `x1` = attributes on |

Common graphics modes (HRES, CRES): 640x2 (101, 00), 640x4 (111, 01),
320x4 (101, 01), 320x16 (111, 10), 256x16 (110, 10). Text with
attributes uses two bytes per cell: character, then attribute
(b7 blink, b6 underline, b5-3 foreground `$FFB0-$FFB7`, b2-0 background
`$FFB8-$FFBF`).

### `$FF9A` Border colour (3)

Bits 5-0, palette format (see `$FFB0`). CoCo 3 modes only.

### `$FF9B` Disto 2 Meg bank (3 (upgrade))

Bits 1-0 select the 512K bank on Disto 2 MB upgrades.

### `$FF9C` Vertical smooth scroll (3)

Bits 3-0: scan line offset within a text row.

### `$FF9D-$FF9E` Vertical offset (screen start) (3)

16-bit value = (GIME physical address) / 8. `$FF9D` = address bits 18-11,
`$FF9E` = bits 10-3. On a 128K machine video lives in `$60000-$7FFFF`.
Some GIMEs crash on odd `$FF9E` values in some modes; step by 16 bytes
and use `$FF9F` for the remainder.

### `$FF9F` Horizontal offset (3)

| Bit | Meaning |
|-----|---------|
| 7   | HVEN: 1 = 256-byte virtual row (horizontal hardware scroll) |
| 6-0 | Offset in 2-byte steps from the `$FF9D/$FF9E` start |

### `$FFA0-$FFAF` MMU (3)

Eight bank registers per task. Each holds a 6-bit 8K page number
(`$30-$3F` on 128K; `$00-$3F` on 512K; on 128K, `$00-$2F` alias
`$30-$3F`). Mask the top two bits when reading.

| Task 0 | Task 1 | CPU range | Power-up page |
|--------|--------|-----------|---------------|
| `$FFA0` | `$FFA8` | `$0000-$1FFF` | `$38` |
| `$FFA1` | `$FFA9` | `$2000-$3FFF` | `$39` |
| `$FFA2` | `$FFAA` | `$4000-$5FFF` | `$3A` |
| `$FFA3` | `$FFAB` | `$6000-$7FFF` | `$3B` |
| `$FFA4` | `$FFAC` | `$8000-$9FFF` | `$3C` |
| `$FFA5` | `$FFAD` | `$A000-$BFFF` | `$3D` |
| `$FFA6` | `$FFAE` | `$C000-$DFFF` | `$3E` |
| `$FFA7` | `$FFAF` | `$E000-$FFFF` | `$3F` |

`$FE00-$FFFF` can be held at physical `$7FExx` (INIT0 MC3). Mask
interrupts while remapping, and do not page out the code or stack you
are running on.

### `$FFB0-$FFBF` Palette (3)

16 entries, bits 5-0 (mask bits 7-6 on read). Writing or reading glitches
the display, so change them in the border.

- **RGB**: b5 R1, b4 G1, b3 B1, b2 R0, b1 G0, b0 B0.
- **Composite**: b5-4 intensity, b3-0 hue.

Power-up RGB values: `$FFB0-$FFB7` = `$12 $36 $09 $24 $3F $1B $2D $26`
(green, yellow, blue, red, buff, cyan, magenta, orange);
`$FFB8-$FFBF` = `$00 $12 $00 $3F $00 $12 $00 $26`. Composite differs in
`$FFB1-$FFB3`, `$FFB5`, `$FFB6` (`$24 $0B $07`, `$1F $09`).

---

## SAM (MC6883): `$FFC0-$FFDF`

The SAM has no data bus. Each control bit has two addresses: a write of
any value to the **even** address clears the bit, to the **odd** address
sets it. Write-only; reading returns nothing useful.

| Address | Bit | Meaning |
|---------|-----|---------|
| `$FFC0/$FFC1` | V0 | Display mode (with `$FF22`) |
| `$FFC2/$FFC3` | V1 | |
| `$FFC4/$FFC5` | V2 | |
| `$FFC6/$FFC7` | F0 | Display offset: video start = F x 512 |
| `$FFC8/$FFC9` | F1 | |
| `$FFCA/$FFCB` | F2 | |
| `$FFCC/$FFCD` | F3 | |
| `$FFCE/$FFCF` | F4 | |
| `$FFD0/$FFD1` | F5 | |
| `$FFD2/$FFD3` | F6 | |
| `$FFD4/$FFD5` | P1 | Page #1 (64K, map type 0): swap upper/lower 32K of RAM |
| `$FFD6/$FFD7` | R0 | CPU rate |
| `$FFD8/$FFD9` | R1 | |
| `$FFDA/$FFDB` | M0 | Memory size |
| `$FFDC/$FFDD` | M1 | |
| `$FFDE/$FFDF` | TY | Map type: 0 = ROMs mapped (`$FFDE`), 1 = all-RAM (`$FFDF`) |

**V2 V1 V0 (display mode).** 000 alphanumeric / SG4 / SG6, 001 G1C/G1R,
010 G2C, 011 G2R, 100 G3C, 101 G3R, 110 G6C/G6R, 111 DMA. Full table
with VDG bits, resolutions and RAM sizes: `vdg-modes.md`.

**F6-F0 (display offset).** Text default is F = 2 (`$0400`). The
project's RG6 reservation at `$0600` is F = 3.

**R1 R0 (CPU rate).**

| R1 R0 | Rate |
|-------|------|
| 00 | 0.89 MHz always (default) |
| 01 | Address-dependent: 1.78 MHz for ROM and I/O, 0.89 MHz for RAM. Display stays good. |
| 1x | 1.78 MHz always. On a CoCo 1/2 the CPU takes the video cycles, so the display is garbage |

The usual "speed-up poke" is `$FFD7` (R0 = 1, address-dependent);
`$FFD9` (R1 = 1) is the full-speed mode. On the CoCo 3, `$FFD9` gives
1.78 MHz everywhere with video intact, and `$FFD8` returns to 0.89.

**M1 M0 (memory size).** 00 = 4K, 01 = 16K, 10 = 32K/64K dynamic,
11 = static RAM. Set by the BASIC reset code; changing it later can
corrupt RAM.

**TY.** Tandy's manual says to keep TY clear on a CoCo 1/2. Writing
`$FFDF` pages the BASIC ROMs out for all-RAM mode on a 64K machine; on a
CoCo 3 RAM accesses go through the MMU. Remember: `$FFDF` **sets** TY,
`$FFDE` clears it.

Used by: `lib/vdg.fs` (V and F bits), `src/clock/clock.fs`,
`src/kaleidoscope/kaleidoscope.fs` and `src/rain/rain.fs` (R0),
`kernel/kernel.asm` all-RAM bootstrap and `lib/fujinet.fs` (TY).

---

## Vectors: `$FFE0-$FFFF`

`$FFE0-$FFF1` are reserved. On a CoCo 1/2 the SAM maps `$FFF2-$FFFF` to
the top of the BASIC ROM (`$BFF2-$BFFF`); on a CoCo 3 they come from
the internal ROM. The ROM vectors point at RAM jump slots you can patch.

| Vector | CPU reads | CoCo 1/2 RAM slot | CoCo 3 points to | Then jumps via |
|--------|-----------|-------------------|------------------|----------------|
| SWI3  | `$FFF2` | `$0100` | `$FEEE` | `$0100` |
| SWI2  | `$FFF4` | `$0103` | `$FEF1` | `$0103` |
| FIRQ  | `$FFF6` | `$010F` | `$FEF4` | `$010F` |
| IRQ   | `$FFF8` | `$010C` | `$FEF7` | `$010C` |
| SWI   | `$FFFA` | `$0106` | `$FEFA` | `$0106` |
| NMI   | `$FFFC` | `$0109` | `$FEFD` | `$0109` |
| RESET | `$FFFE` | `$A027` (ROM) | `$8C1B` | |

Each RAM slot is a 3-byte `JMP` (or `LBRA`). Mask interrupts while you
replace one, and restore the original before returning to BASIC.

---

## Corrections to the source

Places where Lomont v0.82 is wrong or unclear, and what this page uses:

1. **SAM M0/M1 addresses.** Lomont's `$FFDA-$FFDD` entry repeats the
   R0/R1 addresses (`$FFD6-$FFD9`) in its body. Tandy's map confirms
   M0 = `$FFDA/$FFDB`, M1 = `$FFDC/$FFDD`.
2. **SAM V2 label.** Lomont writes `$FFC4/5` as "V2CLR/V1SET". It is
   V2 clear / V2 set.
3. **SG4 colour 0.** Lomont lists colour 000 as cyan. It is green
   (green, yellow, blue, red, buff, cyan, magenta, orange), as in
   CLAUDE.md and `vdg-modes.md`.
4. **R1/R0 rates.** Lomont leaves the meaning of 01 and 11 as TODO. The
   table above follows the MC6883 behaviour: 01 is address-dependent,
   1x is full speed.
5. **`$FF22` bit 3.** Lomont lists both "RGB monitor sensing (input)" and
   CSS. Tandy's map gives CSS output only. Not checked on a CoCo 3.
6. **`$FF22` bit 4 on the T1.** Neither source covers the MC6847T1; on
   the CoCo 2B it is lowercase, not SG6 (#573, measured).
7. **CoCo 1/2 vectors.** Lomont's vector table gives the CoCo 3 `$FExx`
   targets only; the CoCo 1/2 ROM vectors point straight at the `$01xx`
   RAM slots.

Gaps the source marks TODO and this page does not fill: SuperVoice and
CyberVoice register layout, TC^3 SCSI addresses, LR-Tech SASI, Disto
SCII, the exact meaning of `$FF9C` value 0.
