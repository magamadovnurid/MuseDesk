# Third-party components

Muse Desk is an independent application, not an official OpenAI product.
No blanket open-source license is assigned by this import. The repository owner
must choose the license for their original code separately.

## Interface assets

`windows-native/assets/icons/sources.json` records each icon's origin.
The existing UI is preserved: 23 SVG icons originate from an installed Codex
26.901.6511.0 application. Their redistribution license has **not been verified**;
this import does not grant rights to those third-party assets. Review permission
or replace those icons before distributing the application to third parties.
The embedded `icons.zip` contains raster derivatives of the same SVG files.

`check.svg` and `spark.svg` originate from the public OpenAI Apps SDK UI library:
https://github.com/openai/apps-sdk-ui (MIT). Its license is included at
`windows-native/assets/icons/LICENSE-openai-apps-sdk-ui.txt`.
Segoe UI and Windows speech components are supplied by Windows, not this repository.

## Optional build dependencies and runtime

- Sharp: Apache-2.0, https://github.com/lovell/sharp (see its dependency notices).
- JSZip: MIT or GPLv3, https://github.com/Stuk/jszip.
- Ollama: https://github.com/ollama/ollama; downloaded separately, includes its
  own license and dependency notices. Do not strip notices from its distribution.
- Model: https://ollama.com/acc100/muse-glimmer-heretic. Weights are not stored
  here. The model publisher's terms and upstream model terms apply separately.
- Experimental Mac client requires Electron; no Electron binary is included.
