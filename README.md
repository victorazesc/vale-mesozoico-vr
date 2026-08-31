# Vale Mesozoico VR

Protótipo original de montanha-russa VR para Meta Quest 3, ambientado em um vale pré-histórico. O projeto usa Unity 6.3 LTS, URP e OpenXR e contém uma única fase curta para teste.

## Abrir e gerar

1. Abra a pasta no Unity Hub com Unity `6000.3.23f1` e Android Build Support.
2. Aguarde a restauração dos pacotes.
3. Execute `Vale Mesozoico > Preparar projeto`.
4. Execute `Vale Mesozoico > Gerar APK Quest`.

Também é possível gerar em batch mode:

```sh
"/Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents/MacOS/Unity" \
  -batchmode -quit \
  -projectPath /Users/victorazevedo/projetos/vale-mesozoico-vr \
  -executeMethod ValeMesozoico.Editor.QuestProjectBuilder.BuildQuestApk \
  -logFile Logs/build-quest.log
```

Saída esperada: `Builds/Quest/ValeMesozoicoVR.apk`.

## Instalar o APK pronto

Com o Quest 3 em modo desenvolvedor, conectado por USB e autorizado:

```sh
"/Applications/Unity/Hub/Editor/6000.3.23f1/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb" \
  install -r Builds/Quest/ValeMesozoicoVR.apk
```

O APK usa assinatura de desenvolvimento, adequada para sideload e testes internos.

## Controles e conforto

- Passeio automático, sentado, com rastreamento 6DoF da cabeça.
- Sem loops, inversões, tremor artificial ou quedas verticais.
- 72 Hz, MSAA 4x, Vulkan, ARM64 e IL2CPP.
- Ao terminar, a fase reinicia após uma pausa curta.

## Originalidade

O projeto é inspirado apenas no gênero de montanha-russa VR. Não inclui marca, logo, código, layout, trilho, áudio ou assets da B4T/Epic Roller Coasters ou de franquias de cinema.
