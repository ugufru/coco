# RG6 Graphics Test

A visual test for the RG6 artifact-colour libraries: five screens, one key
between each (BREAK quits).

```
make run    # runs in XRoar with NTSC artifact colours (-tv-input cmp-br)
```

1. **Bars**: horizontal bands in white, blue and red with `rg-pset`.
2. **Lines**: a star of `rg-line`s from the centre: straight, diagonal and
   shallow, in three colours.
3. **Sprites**: an Endever ship and three Jovians drawn with `spr-draw`,
   inside a white border.
4. **Erase**: the middle ship and Jovian cleared with `spr-erase-box`.
5. **Angles**: lines every 30 degrees from the centre with `angle-dx` and
   `angle-dy`, the way a maser beam is aimed.

Libraries exercised: `lib/rg-pixel.fs`, `lib/sprite.fs`,
`lib/datawrite.fs` (to build the sprite data), `lib/trig.fs`, `lib/vdg.fs`.

The colours only appear on an NTSC composite picture, where pairs of
pixels blend into artifact colours: XRoar needs `-tv-input cmp-br` (make
run passes it), and on an RGB monitor or a PAL machine the screen is
black and white stripes. It is on `build/demos.dsk` as `RG-TEST`.
