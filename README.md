# LB Generator — 의료기기 라벨 PDF 생성기

의료기기 라벨 제작용 엑셀 매크로 파일(`.xlsm`)의 기능을 브라우저 단독 실행형 웹앱으로 재구현한 프로그램입니다.
프린터 없이 라벨을 동일한 레이아웃으로 **실측 크기의 PDF**로 출력하는 것이 목적입니다.

## 실행 방법

별도 서버/설치 없이 동작합니다. **Chrome 또는 Edge**에서 `index.html`을 열면 됩니다.

> 폴더 연동(이미지 폴더, PDF 저장 폴더)은 File System Access API를 사용하므로 Chrome/Edge 계열이 필요합니다.
> 네트워크 드라이브는 Windows에 드라이브 문자로 매핑(예: `Z:\Images`)하면 폴더 선택 창에서 그대로 선택할 수 있습니다.

로컬 서버로 여는 것도 가능합니다:

```
python -m http.server 8000
# http://localhost:8000 접속
```

## 기능

| 기능 | 설명 | 원본 엑셀 대응 |
|---|---|---|
| DB 로딩 | `.xlsm/.xlsx/.csv`에서 `라벨DB` 시트 로딩, 브라우저(IndexedDB)에 저장되어 재방문 시 자동 복원 | `라벨DB` 시트 (H열 품목번호 키) |
| 사용자 입력 | LOT, SN, MFG, 유효기간(개월) 입력. EXP는 `EDATE(MFG, n) - 1일` 자동 계산(수동 전환 가능) | `B2, D2, D1`, `=EDATE(D1,36)-1` |
| DB 참조 값 | 품목번호로 제품명·규격·GTIN·치수·이미지 파일명 등 자동 조회 | `INDEX/MATCH(... 라벨DB!H:H ...)` |
| UDI | `(01)GTIN(10)LOT(17)YYMMDD(240)품목번호(21)SN` 자동 조합 (빈 그룹은 생략) | `D6` 수식 |
| GS1 DataMatrix | UDI를 DataMatrix 바코드로 생성 (bwip-js, **오프라인 동작**) | VBA `GenerateBarcode` (bwip-js API 호출) |
| 라벨 편집기 | 휠 줌 / 드래그 패닝, 객체 드래그 이동·핸들 리사이즈, 다중 선택(Shift+클릭), 방향키 미세이동 | 시트 위 도형 배치 |
| 이미지 슬롯 | 영역을 사전 지정해 두면 DB의 파일명으로 이미지 폴더에서 자동 로딩. **종횡비 고정** 후 좌/중앙/우 정렬 선택 | VBA `UpdateImagesKeepAspectRatio` + `ImgConfig` 시트 |
| 배경 투명화 | 투명 PNG는 그대로, JPG·불투명 PNG는 가장자리 연결 영역만 플러드필로 자동 투명 처리(제품 내부 흰색 보존) | (신규) |
| 텍스트 서식 | 폰트·크기(pt)·굵게·기울임·자간·문단정렬을 객체별/선택 객체 일괄 적용 | 텍스트박스 서식 |
| 라벨 크기 | 가로/세로(mm) 직접 입력 | 인쇄 영역 |
| PDF 출력 | 라벨 실측 크기(mm) PDF 생성 (300/600/1200 DPI). 저장 폴더 사전 지정 + 파일명 규칙 자동 생성 | 프린터 출력 대체 |
| 템플릿 | 레이아웃 자동 저장(IndexedDB) + JSON 내보내기/가져오기 | `ImgConfig` 숨김 시트 |

## 텍스트 플레이스홀더

텍스트 객체 내용에 아래 플레이스홀더를 쓰면 현재 선택된 품목/입력값으로 치환됩니다.

- 입력값: `{LOT}` `{SN}` `{MFG}` `{EXP}` `{EXP6}` (YYMMDD) `{ITEM}` (품목번호)
- DB 참조: `{PRODUCT}` `{MDR}` `{REF}` `{GTIN}` `{STENT_OD}` `{STENT_LEN}` `{HEAD_OD}` `{HEAD_LEN_D}` `{HEAD_LEN_P}` `{GW_INCH}` `{GW_MM}` `{DD_FR}` `{DD_MM}` `{DD_LEN}` `{LIFETIME}` `{COVER}` `{KOREA_NO}` `{KOREA_NAME}`
- UDI: `{UDI_FULL}` `{UDI_L1}` `{UDI_L2}` `{GTIN01}`
- 임의 열 직접 참조: `{@AJ}` 처럼 `@` + 라벨DB 열문자

## 파일명 규칙

설정의 파일명 규칙에 플레이스홀더 사용: `{ITEM} {REF} {LOT} {SN} {MFG} {EXP} {EXP6} {DATE} {TIME} {PRODUCT} {W} {H}`
예: `{ITEM}_{LOT}_{DATE}` → `16-0401_26041086_260729.pdf`

## 원본 엑셀 분석 요약

- **`PML-001 Rev.1` 시트**: 품목번호(B1)·LOT(B2)·SN(D2)·제조일(D1)을 입력하면 `INDEX/MATCH`로 `라벨DB`를 조회해 라벨 필드를 채우고, `D6`에서 GS1 UDI 문자열을 조합. 라벨 레이아웃은 시트 위 100여 개의 텍스트박스/그림 도형으로 구성.
- **`라벨DB` 시트**: 약 12,000행 × 55열. H열(품목번호)이 조회 키.
- **VBA `Sheet7`**: `D6` 값 변경을 감지해 bwip-js 웹 API(`gs1datamatrix`)로 DataMatrix PNG를 받아 `DM_*` 도형을 교체.
- **VBA `Module1.UpdateImagesKeepAspectRatio`**: `C:\Images\` 폴더에서 C14:C19의 파일명으로 이미지를 로딩, 높이 기준 종횡비 고정 배치. 사용자가 수동으로 옮긴 위치/크기는 숨김 시트 `ImgConfig`에 저장되어 유지.

본 앱은 위 로직(수식·VBA)을 모두 클라이언트 사이드 JavaScript로 재구현했으며, 바코드 생성은 외부 API 대신 bwip-js 라이브러리를 내장하여 오프라인에서도 동작합니다.

## 폴더 구조

```
index.html        앱 진입점
css/style.css
js/store.js       IndexedDB 영속화
js/imaging.js     이미지 로딩·배경 자동 투명화
js/editor.js      캔버스 라벨 편집기 (줌/패닝/드래그/리사이즈)
js/exporter.js    실측 크기 PDF 출력 (jsPDF)
js/app.js         메인 로직 (DB 파싱, 필드 계산, UDI, UI)
libs/             SheetJS · bwip-js · jsPDF (오프라인 번들)
```
