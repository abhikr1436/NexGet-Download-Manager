set -e
cd /home/claude/ff/FFmpeg-n9.0.2
./configure --target-os=mingw32 --arch=x86_64 --cross-prefix=x86_64-w64-mingw32- --prefix=/home/claude/ff/out \
  --enable-static --disable-shared --disable-debug --disable-doc --disable-ffplay --disable-autodetect \
  --disable-everything --disable-x86asm --enable-small --enable-schannel \
  --extra-ldflags="-static -s" --pkg-config-flags=--static \
  --enable-protocol=file,pipe,data,http,https,httpproxy,tcp,tls,hls,crypto,concat,subfile \
  --enable-demuxer=mov,matroska,mpegts,hls,aac,mp3,flac,ogg,wav,webvtt,srt,ass,concat,h264,hevc,m4v,mpegps,flv,dash,ac3,eac3,image2,mjpeg,png_pipe,jpeg_pipe,webp_pipe \
  --enable-muxer=mp4,mov,ipod,matroska,webm,mp3,mpegts,adts,ogg,opus,flac,wav,null,webvtt,srt,ass,ac3,eac3,m4a,image2,mjpeg \
  --enable-parser=aac,aac_latm,h264,hevc,vp8,vp9,av1,opus,vorbis,mpegaudio,ac3,flac,mjpeg,png,webp,dca \
  --enable-bsf=aac_adtstoasc,h264_mp4toannexb,hevc_mp4toannexb,extract_extradata,vp9_superframe,vp9_superframe_split,av1_frame_merge,av1_frame_split,null,setts,dump_extradata \
  --enable-decoder=aac,mp3,opus,vorbis,flac,pcm_s16le,mjpeg,png,webvtt,srt,ass,subrip,mov_text \
  --enable-encoder=mjpeg,png,webvtt,srt,ass,subrip,mov_text,pcm_s16le \
  --enable-filter=null,anull,copy,acopy,aresample,scale,format,aformat \
  --enable-indev=lavfi --enable-swscale --enable-swresample \
  > /home/claude/ff/configure.log 2>&1
make -j8 > /home/claude/ff/make.log 2>&1
make install > /home/claude/ff/install.log 2>&1
echo BUILD_OK
