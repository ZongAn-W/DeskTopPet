# Runtime video assets

The desktop pet can play transparent `.mov` clips at runtime when FFmpeg is available. Put the
clips in this folder and put a compatible `ffmpeg.exe` here, beside the published executable.
`publish.ps1` copies the working copy's `videos/*.mov` files here automatically. It does not copy an
FFmpeg executable; install FFmpeg separately or provide a build whose license and redistribution
terms fit your distribution.

The current clip names are `idle-blink.mov`, `sigh.mov`, `turn.mov`, `walk-start.mov`,
`walk-loop.mov`, and `walk-stop.mov`. The player uses the same 180 x 210 canvas mapping as the
committed PNG frames and preserves the source alpha channel. If a clip or FFmpeg is missing, the
corresponding PNG frames or vector placeholder remain available as a fallback.
