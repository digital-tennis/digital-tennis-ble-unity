# BLE SDK와 플랫폼 경계

`Runtime/`은 UnityEngine에 의존하지 않는 netstandard2.1이다. 게임은 플랫폼 구현을 직접 참조하지 않고 `BleControllerClient`의 상태·기기·정규화 의도만 읽는다. 프로토콜 PR1의 커밋 `d9a36826afbafa27a0e276cc53e44c904d05be53` (현재 검토 브랜치 커밋; 스쿼시 병합 후 최종 SHA 갱신)을 고정해 사용한다.

```text
Native/mac(CoreBluetooth) 또는 Native/windows(Windows GATT)
  → 20바이트 Notify / PC 단조 수신시각 / 고정 operation
  → IBleTransport → BleControllerClient → RacketIntent
  → 게임 IControllerBackend → PlayerInputRouter → 일반 타격 판정
```

Native helper는 기기 검색을 요청한 때만 시작한다. 프로덕션에 가짜 장치·자동 mock 대체가 없다. 검색/연결마다 새로운 프로세스와 operation을 발급하므로 종료한 worker의 늦은 콜백은 새 연결이 되지 못한다. `ClearInput`은 스윙만 비우고 ACK/상태·연결 작업은 보존한다. `Disconnect`는 해당 helper와 연결 작업을 종료한다. 외부 프로그램이나 다른 앱을 종료하지 않는다.

Mac은 서비스 UUID 필터 검색→선택한 UUID 검색 기록→connect→서비스/Notify/Write 확인→OS 페어링·암호화 CCC 구독 후에만 connected를 보낸다. `NSBluetoothAlwaysUsageDescription`을 helper에 포함한다. Bluetooth가 꺼지거나 거부되면 미지원/오류를 표시한다. Windows는 서비스 광고를 필터하고 선택 주소만 연결한다. OS 동의가 가능한 STA UI 스레드에서 GATT·PairAsync를 실행하며 자동 승인·페어링 삭제를 하지 않는다. 알림 구독 성공이 연결 완료 조건이다.

연결 후 별도로 `Calibrate(now)`를 요청한다. 같은 연결의 명령ID와5초 제한에 맞는 성공ACK와 ready/calibrated flags가 모두 필요하다. 상태패킷·과거 ACK만으로 새 보정이 끝나지 않는다. 실패·timeout은 보정 미완료로 남고 사용자가 다시 요청할 수 있다. 보정 완료 전·보정 진행 중의 스윙은 차단한다.

알림은 실제 worker 수신 시 PC 단조시계로 찍는다. SDK drain 시각이나 MCU `deviceTimeMs`로 새 입력처럼 만들지 않는다. 게임의 시계 기준과 맞춘 thread-safe `Func<double>`을 조립 시 주입한다. 200ms 초과/20ms 미래/NaN, 오류패킷, 이전 operation, 중복/오래된 순번과 최근64개 중복 이벤트를 거부한다. 게임의 기존 라켓 도달 범위·차례·stock 검사는 다시 실행된다. 연결 종료 시 epoch·보정·대기 이벤트를 폐기하며 누르고 있던 스킬을 새 입력으로 재생하지 않는다.

## 소프트웨어 검사·배포

```sh
dotnet run --project tests/Checks.csproj -c Release -p:ProtocolRoot=/absolute/protocol
python3 tools/build-native.py --platform mac --out /absolute/native/mac
python3 tools/build-native.py --platform windows --out /absolute/native/windows
```

Mac helper는arm64/x86_64 universal 바이너리이고 사용 목적 plist를 포함한다. Windows helper는 .NET self-contained x64 배포다. 게임 빌드 후처리가 해당 플랫폼 출력만 `StreamingAssets/ControllerNative/`에 넣는다. 네이티브 바이너리는 Git에 커밋하지 않는다. CLI 검사는 실제 전파 검색/페어링을 시작하지 않는다.

가짜 transport는 `tests/`에만 있고 게임의 개발 E2E는 명시적 검증 옵션으로만 주입한다. 현재 검증은SDK31개·Swift/macOS universal 컴파일·Windows 교차 컴파일이다. **실제 외장 IMU·라디오·OS 동의 팝업·실물 WindowsPC 검증은 유예**다. 검증된 컴파일을 실물 연결 성공으로 표시하지 않는다. 원래12일·3명·NU40-DK+별도 외장IMU 조건을 유지한다.

기술 기준: [Apple CBCentralManager](https://developer.apple.com/documentation/corebluetooth/cbcentralmanager), [Microsoft Bluetooth GATT Client](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/gatt-client). SDK 소비자는 지원 OS 권한과 선택한 기기만 사용한다.

## 센서 없는 통신 확인

`ProbeLink(now)`는 v2 LINK_PROBE를 보내고 현재 연결/nonce의 LINK_REPLY를2초 동안 기다린다. Write-with-response 성공, Notify 구독, 실제 앱 응답은 독립 단계다. 이 확인은 보정 상태를 바꾸거나 라켓 입력/스킬/설정 저장을 만들지 않는다. 잘못된 nonce·늦은 응답·끊김은 성공을 무효화한다. 게임은 연결 상세에서 이 상태를 보여주며 외장 IMU가 없어도 링크 시험을 할 수 있다.

사용자가 제공한 NU-54 제품의 `p01-device-firmware/app/src/ble_links.c`와 `p05-operations-backoffice/internal/ble/central.go`, 참조 커밋 `ee12b7519336ed276060b83c69f4f863f557443a`의 서비스 발견·구독 확인·순차 Write-with-response·연결 수명 패턴을 참고했다. 테니스 서비스/패킷으로 구현하며 참조 제품의 결제/프로비저닝 기능은 포함하지 않는다. Mac/Windows helper의 명령 쓰기는 최대16개 큐에서 응답을 기다려 직렬화한다.

```sh
python3 tools/probe-link.py --helper .work/native/mac/digital-tennis-ble --scan --out .work/scan.json
python3 tools/probe-link.py --helper .work/native/mac/digital-tennis-ble --device '선택한 ID' --out .work/link.json
```

이 도구는 명시적으로 실행할 때만 Bluetooth를 사용한다. 참조 NU-54의 다른 UUID/애플리케이션 프로토콜은 게임 라켓과 자동 호환되지 않는다. 실제 참고 보드에서의 연결/응답 결과와 테니스 20바이트 패킷의 모의 전파 검증은 별도 증거로 기록한다.
