# Editor Toolkit

커스텀 Attribute + 에디터 프레임워크 + SceneBookmark + 유틸리티

## 설치

```
"com.tjdtjq5.editor-toolkit": "https://github.com/tjdtjq5/unity-packages.git?path=com.tjdtjq5.editor-toolkit#editor-toolkit/v1.3.2"
```

## 주요 기능

### Inspector Attributes (23종)
`[InspectorButton]`, `[BoxGroup]`, `[StyledList]`, `[SectionHeader]`, `[ShowIf]`, `[HideIf]`, `[DisableIf]`, `[Required]`, `[ReadOnlyField]`, `[Preview]`, `[ProgressBar]`, `[MinMaxSlider]`, `[HelpBox]`, `[Separator]`, `[Indent]`, `[LabelWidth]`, `[AssetPath]`, `[LayerSelector]`, `[TagSelector]`, `[TypeSelector]`, `[SerializeReferenceSelector]`, `[OnValueChanged]`

모든 MonoBehaviour에 자동 적용 — 별도 CustomEditor 클래스 불필요.

### Editor Framework
- `EditorTabBase` — 탭 기반 윈도우 공통 베이스
- `SceneBookmarkToolbar` — 메인 툴바 씬 북마크
- `GameSpeedToolbar` — 메인 툴바 게임 속도 슬라이더
- `EditorPlayShortcuts` — F10/F11/F12 글로벌 단축키
- `DomainReloadValidator` — static 필드 리셋 누락 감지

## 요구사항
- Unity 6000.1+
