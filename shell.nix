{ pkgs ? import <nixpkgs> {} }:

pkgs.mkShell {
  packages = with pkgs; [
    dotnet-sdk_8
    gcc
    glibc
    zlib
    libGL
    fontconfig
  ];

  shellHook = ''
    export DOTNET_CLI_TELEMETRY_OPTOUT=1
    export DOTNET_NOLOGO=1
  '';
}