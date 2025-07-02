@echo off
cd /d Z:\Downloads
skopeo copy "docker://docker.io/randomdude/gcc-cross-x86_64-elf:latest" "docker-archive:cross.tar"
pause