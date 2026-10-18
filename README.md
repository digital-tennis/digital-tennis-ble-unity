# Digital Tennis · BLE 컨트롤러 SDK

Mac/Windows의 Bluetooth 연결과 본딩을 확인하고, 검증한 라켓 이벤트를 Unity 비의존 입력 의도로 전달한다.

전체 제품은 NU40-DK에 **별도 외장 IMU**를 부착하는 라켓 컨트롤러와 Unity PC 테니스 게임으로 구성한다. 최초 계획은 **12일·3명**이다.

## 주요 기능

- CoreBluetooth/Windows GATT 검색·OS 페어링·본딩 증거 확인
- 패킷 CRC·시각·순번·중복/이전 연결 거부
- 센서 없는 왕복 진단과 보정 ACK, 직렬 WriteWithResponse

## 빠른 시작

SDK: .NET10 SDK(netstandard2.1 출력). 네이티브 모듈: macOS Xcode 도구/Swift, Windows용 .NET desktop 타깃.

```sh
dotnet run --project tests/Checks.csproj -c Release -p:ProtocolRoot=/absolute/path/to/digital-tennis-protocol
python3 tools/build-native.py --platform mac --out .work/native/mac
```

## 책임과 현재 범위

IMU 샘플을 다시 분류하거나 테니스 판정을 하지 않는다. 연결 완료는 암호화·본딩 증거를 요구하며 센서 보정/설정 저장과 다르다. 정상 경로에 가짜 기기를 추가하지 않는다. Windows 빌드와 실제 Windows 본딩은 구분한다.

## 구조와 문서

`Runtime/`: 순수 C# · `Native/mac`, `Native/windows`: OS 연결 · `tests/`: 모의 전송 검사 · `tools/`: 빌드/진단

[계층과 배포](docs/architecture.md), [전송 구현](Runtime/NativeBleProcessTransport.cs)

## 도움말과 기여

재현 가능한 문제는 [이슈](https://github.com/digital-tennis/digital-tennis-ble-unity/issues)로 남긴다. 관리 주체는 [Digital Tennis](https://github.com/digital-tennis)이며 [기여 안내](CONTRIBUTING.md)를 따른다. 검증 커밋을 고정하고 실제 장치 성공·모의 검사·교차 빌드를 구분한다.

## 라이선스

프로젝트 자체 코드·문서는 **[Apache-2.0](LICENSE)**이다. [NOTICE](NOTICE)와 [외부 코드·자산 고지](THIRD_PARTY_NOTICES.md)를 함께 확인한다. 외부 자산에 Apache-2.0을 덮어쓰지 않는다.
