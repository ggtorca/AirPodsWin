# AirPodsWin

Panel para usar los AirPods en Windows con una interfaz inspirada en iOS/macOS. Vive en la bandeja del
sistema y se abre con un clic.

## Funciones

- **Batería** del auricular izquierdo, del derecho y del estuche, con indicador de carga.
- **Aviso de batería baja** (< 20 %) mediante notificación de Windows.
- **Slider de volumen** estilo iOS, sincronizado con el volumen de Windows.
- **Selector de salida de audio** desde el propio panel.
- Opción "Iniciar con Windows" en el menú del icono de bandeja.

## Cómo funciona

- La batería se lee de los anuncios Bluetooth LE que emiten los AirPods (datos de fabricante de Apple,
  `0x004C`, tipo `0x07`). No requiere drivers.
- El volumen usa Core Audio (NAudio) y el cambio de salida usa la interfaz COM `IPolicyConfig`.

## Limitaciones

- Apple solo transmite la batería en **pasos del 10 %**.
- Los AirPods solo emiten su batería con la **tapa del estuche abierta** o con los auriculares en uso;
  si no, el estuche aparece como "—".
- **El control de ruido (ANC / Transparencia) no es posible**: usa un canal L2CAP propietario de Apple
  y la pila Bluetooth de Windows no lo ofrece a las aplicaciones (`WSAENETDOWN`).
- Con **Smart App Control** activado, Windows bloquea el ejecutable al no estar firmado con un
  certificado reconocido. Hay que desactivarlo o compilar/firmar con un certificado válido.

## Compilar

Requisitos: Windows 10 (19041) o superior y [.NET 8 SDK](https://dotnet.microsoft.com/download).

```powershell
dotnet publish -c Release -r win-x64 --self-contained false -o publish
.\publish\AirPodsWin.exe
```

`AirPodsWin.exe --demo` abre el panel con datos simulados, útil para revisar la interfaz.
