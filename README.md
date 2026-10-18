# Digital Tennis Windows BLE와 Unity 입력 SDK

BLE 검색·연결·재연결, 메인 스레드 입력 전달, 가상 컨트롤러를 담당하는 독립 제품 저장소입니다. 전체 프로젝트는 **외장 IMU를 별도로 부착한 NU40-DK**, Unity PC 게임, 1인 AI와 온라인 2인 대전을 12일·3명으로 개발합니다.

## 현재 상태

Unity 비의존 C# SDK와 Mac CoreBluetooth/Windows GATT 네이티브 전송을 구현했습니다. [계층·검증·배포](docs/architecture.md)를 확인하세요. `dotnet run --project tests/Checks.csproj -c Release -p:ProtocolRoot=/absolute/protocol`로31개 검사를 실행합니다. 실제 연결과 보정 ACK, 동작 테스트는 서로 다른 상태입니다.

정상 사용자 경로는 가짜 장치를 만들지 않습니다. Native helper는 소스에서 별도로 빌드하며 Git에 바이너리를 넣지 않습니다. 컴파일·모의 전송 검사 통과와 실제 OS페어링/라디오/IMU 성공은 구분합니다. 실물 검증은 유예 상태입니다.

## 개발할 기능과 제출 기준

[초보 개발자용 상세 과제](https://github.com/digital-tennis/digital-tennis-docs/blob/main/docs/products/digital-tennis-ble-unity.md)에서 상황·입력·출력·예제·예외·완료 기준을 확인합니다.

- [BL-01 표준 입력과 가상 컨트롤러](https://github.com/digital-tennis/digital-tennis-docs/blob/main/docs/products/digital-tennis-ble-unity.md#bl-01)
- [BL-02 Windows BLE 검색과 Notify 구독](https://github.com/digital-tennis/digital-tennis-docs/blob/main/docs/products/digital-tennis-ble-unity.md#bl-02)
- [BL-03 패킷 검증과 Unity 메인 스레드 전달](https://github.com/digital-tennis/digital-tennis-docs/blob/main/docs/products/digital-tennis-ble-unity.md#bl-03)
- [BL-04 재연결과 보정 UI 및 지연 측정](https://github.com/digital-tennis/digital-tennis-docs/blob/main/docs/products/digital-tennis-ble-unity.md#bl-04)

## 개발 환경과 예상 구조

초기 담당 역할은 C / A 검토입니다. 실제 IMU·배선·펌웨어 SDK·Unity·Windows·런타임 버전은 D1에 [환경표](https://github.com/digital-tennis/digital-tennis-docs/blob/main/docs/project/overview.md)와 결정 기록에 남깁니다.

예상 폴더는 `Runtime/ · Tests/ · Samples~/ · WindowsAdapter/`입니다. 아직 존재하지 않는 폴더와 실행 명령을 구현 완료로 해석하지 않습니다. 채택 도구와 실제 구현에 맞게 갱신합니다.

## 검증과 협업

- [12일 WBS](https://github.com/digital-tennis/digital-tennis-docs/blob/main/docs/planning/wbs.md)
- [컨트롤러 계약](https://github.com/digital-tennis/digital-tennis-docs/blob/main/docs/contracts/controller-protocol.md)
- [온라인 계약](https://github.com/digital-tennis/digital-tennis-docs/blob/main/docs/contracts/online-protocol.md)
- [수용 테스트](https://github.com/digital-tennis/digital-tennis-docs/blob/main/docs/testing/acceptance.md)
- [브랜치·PR 작업 방법](https://github.com/digital-tennis/digital-tennis-docs/blob/main/docs/guides/contributing.md)
- [모든 제품과 의존 관계](https://github.com/digital-tennis/digital-tennis-docs/blob/main/docs/project/repositories.md)

라이선스는 코드와 제3자 자산 조건을 검토한 뒤 팀이 선택합니다.
