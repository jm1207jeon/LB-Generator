# LaPrint (C#) — 디자인 · HFE 규칙

UDInspect(`/home/user/udinspect`) · LaVis/LabelSuite(`/home/user/jm1207jeon/lbinspt/csharp`) 와 **한 가족처럼 보이고 똑같이 동작**해야 한다.
두 앱을 실제로 읽어 정리한 규칙이다. 브라우저판 `css/style.css` 의 토큰도 같은 뿌리다.

## 1. 기술 관례 (두 앱 공통)

| 항목 | 관례 | LaPrint |
|---|---|---|
| UI | WPF, **코드비하인드** (MVVM 프레임워크·DI 없음), `MainWindow` 를 관심사별 partial 파일로 분리 | 동일. `MainWindow.xaml.cs` + `MainWindow.Job.cs / .Stage.cs / .Inspector.cs / .Queue.cs / .Print.cs / .Settings.cs` |
| 상태 | 화면 컨트롤은 `x:Name`, 목록은 `ObservableCollection<T>` of POCO | 동일 |
| 설정 | `%APPDATA%\<앱>\settings.json`, System.Text.Json 들여쓰기 + `UnsafeRelaxedJsonEscaping`, tmp→Move 원자 저장, 600 ms 디바운스 자동 저장 | `%APPDATA%\LaPrint\` (Core.Storage) |
| 로그 | `app.log` 1 MB 회전, `yyyy-MM-dd HH:mm:ss.fff [LEVEL] msg` | 동일 (`AppLog`) |
| 오류 | 상태줄 3단계 + `DispatcherUnhandledException` → MessageBox(로그 경로 안내) 후 계속 실행, 단일 인스턴스 Mutex | 동일 (`Local\LaPrint_SingleInstance`) |
| 창 | 메인 1400×880 (최소 1280×720) CenterScreen; 설정 1000×820 CenterOwner ShowInTaskbar=False; 대화상자 460~520 SizeToContent NoResize | 동일 |
| DPI | `app.manifest` PerMonitorV2 (LaVis) | 동일 |
| 테마 | LaVis 처럼 `Theme.xaml` ResourceDictionary 에 이름 있는 브러시·스타일. UDInspect 는 리터럴이라 그 값만 가져온다 | `Theme.xaml` |

## 2. 색 토큰 (`Theme.xaml` 의 x:Key)

```
InkBrush        #1F2A37   본문 글자          Ink2Brush #4B5563   Ink3Brush #6B7280 (힌트 #666666 과 동등)
GroundBrush     #FFFFFF   카드/입력 배경     PaperBrush #F4F6F9  패널      Paper2Brush #E9EEF4  헤더/탭
LineBrush       #CCD3DB   테두리             Line2Brush #E2E7ED  격자선     GridLineBrush #DDDDDD (DataGrid)
AccentBrush     #1A6FB5   주 버튼·선택·링크   AccentDeepBrush #1A5276  강조 글자   AccentSoftBrush #E3EEF8 선택 배경
PassBrush       #2E9E5B   합격/완료          PassFillBrush #D7EFD7
FailBrush       #9C0006   오류 글자          FailFillBrush #FFC7CE   DangerBrush #D9534F 파괴적 버튼   ErrorStatusBrush #B01E1E
WarnBrush       #9A5B00   경고 글자          WarnFillBrush #FFF4D6   WarnLineBrush #E0B25A
OrangeBrush     #E67E00   BSC 표시·강조 숫자  CautionBrush #F0AD4E  주의 버튼
CanvasBgBrush   #8A97A6   캔버스 바탕        CanvasEdgeBrush #4B5563  라벨 외곽   SnapGuideBrush #E0218A
```
반드시 **텍스트로도** 뜻이 전달되어야 한다(색만으로 구분 금지): 상태줄 문장, `✓ / ⚠ / ✕` 기호, "오류 2" 같은 개수.

## 3. 글꼴 · 크기 · 형태

- 글꼴은 지정하지 않는다 → Segoe UI / 맑은 고딕(시스템). 숫자·코드·품목번호·LOT 입력칸은 `Consolas`.
- 크기: 11 힌트 · 12 부제/칩 · **13 본문·DataGrid(RowHeight 24)** · 14 라벨 · 15 입력 · 17~18 카드 제목 · 20~24 큰 값.
- 모서리: 4 칩/경고 상자 · 6 패널/카드 · 8 프레임 · 10 오버레이.
- 간격: 4/6/8/10/12/14/16. GroupBox Margin 4 Padding 6. 버튼 Padding 10,5 Margin 2.
- 버튼 변형: PRIMARY `#1A6FB5` 흰 굵은 글자 · DESTRUCTIVE `#D9534F` 흰 굵은 글자, **오른쪽 끝에 24px 띄워 분리** · CAUTION `#F0AD4E` 검은 굵은 글자 · 보통 = 기본 크롬.
- DataGrid: AutoGenerateColumns=False, HeadersVisibility=Column, RowHeaderWidth=0, RowHeight=24, FontSize=13, 격자선 `#DDDDDD`, 행 색은 DataTrigger.
- 그림자: 오버레이 BlurRadius 18 / ShadowDepth 4 / Opacity 0.35.

## 4. HFE 규칙 (IEC 62366 — 의료기기 라벨은 오출력이 곧 회수)

1. **모드 오류 금지** — `이 라벨 1장 출력`(Ctrl+P) 과 `큐 N장 출력`(Ctrl+Shift+P) 은 언제나 다른 버튼. 큐가 비면 큐 버튼은 숨긴다(뜻이 바뀌지 않는다).
2. **출고 구분(일반/BSC)** 이 평소와 다르면 작업 입력 위에 주황 `BSC` 배지 + 콤보 주황 테두리. DB 칩에도 구분 이름을 쓴다.
3. **확인 대화상자** — 개수·저장 상태·되돌릴 수 없음을 적고 물음표로 끝난다. 기본 버튼은 언제나 **아니오/취소**. Esc = 취소.
   예) `큐 {n}행 · 총 {m}장을 출력합니다.\n오류 행 {k}건은 건너뜁니다.\n\n출력할까요?`
4. **파괴적 동작 색 분리** — 큐 비우기/서식 되돌리기는 `#D9534F`, 오른쪽 끝, 확인창 필수.
5. **잠금이 기본** — 시작 시 레이아웃 잠금. 편집하려면 상단 `✎ 편집` 을 눌러 풀고, 잠금 해제 상태에서 바꾼 레이아웃은 "검증되지 않은 서식" 경고를 출력 전 점검에 띄운다.
6. **출력 전 점검** 은 항상 보인다(좌측 패널). 오류가 있으면 1장 출력 버튼 비활성 + 힌트 `오류를 해결해야 1장 출력을 할 수 있습니다`.
7. **미저장 표시** — 상태줄 오른쪽 `변경됨` / `저장됨 HH:MM`.
8. 단축키는 Ctrl 조합만(스캐너 타이핑 충돌 방지). 글자 단독 키 없음. 목록: Ctrl+P · Ctrl+Shift+P · Ctrl+Enter · Ctrl+Z/Y · Ctrl+0 · Ctrl+D · Delete · 방향키 · Esc.
9. 큐 실행 중에는 입력·서식 변경 잠금, 일시정지/중지만 가능. 진행률과 현재 행(`3/12 출력 중: 16-0401 LOT …`)을 상태줄에.
10. **행마다 그림 교체** 검증 결과를 큐 완료 요약에 포함하지 않아도 되지만, 테스트로 보장한다.

## 5. 상태줄 · 문구

- `[HH:mm:ss] 메시지`. Info 검정 / Warn `#9A5B00` 굵게 / Error `#B01E1E` 굵게. 툴팁 = 전체 문장. Warn·Error 는 로그.
- 문장 규칙: 결과 `~했습니다.` / 경고 `원인 - 조치` / 오류 `{동작} 실패: {원인}` / 경로는 `[⚙ 설정 › 폴더 경로]` 꼴.
- 버튼은 2~4음절 명사구: 저장 · 취소 · 확인 · 적용 · 닫기 · 추가 · 삭제 · 찾아보기 · 폴더 열기 · 지금 읽기 · 떼어내기.
- 브라우저판(`js/app.js`, `js/ui.js`, `index.html`)의 한국어 문구를 **그대로** 옮긴다. 새로 짓지 않는다.

## 6. 화면 구성 (브라우저판 index.html 과 동일)

`ARCHITECTURE.md §3` 의 배치도를 따른다. 좌측 레일 330px · 우측 인스펙터 308px · 앱바 46px · 상태줄 28px.
캔버스는 `SkiaSharp.Views.WPF.SKElement` 하나로, Core 의 `LabelRenderer.Render` 로 라벨을 그린 뒤 편집 장식(선택 테두리 `#1A6FB5` 점선, 8px 핸들, 스냅 안내선 `#E0218A`, 눈금자, 격자, 링크 오버레이 주황 `#E67E00`)을 App 이 덧그린다.

## 7. 아이콘

`src/LaPrint.App/Assets/laprint.ico` (16/24/32/48/64/128/256 PNG 프레임). UDInspect 와 같은 요소 — DataMatrix 격자 `#222B36`, 원형 배지 `#1A6FB5` 테두리 / `#E8F3FC` 채움, 라벨 태그 + 초록 `#2E9E4F` 화살표. `<ApplicationIcon>` 으로만 연결하고 창에는 따로 지정하지 않는다(모든 창이 exe 아이콘을 상속).
