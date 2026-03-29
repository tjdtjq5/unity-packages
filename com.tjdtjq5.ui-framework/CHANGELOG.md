# Changelog

## [2.0.0] - 2026-03-29

### Breaking Changes
- Popup 시스템 전체 제거 (UIPopup, UIManager, UITransition, UIDialog, UILifetimeScope)
- VContainer 의존성 제거
- UIFollowWorld, UIProgressBar, UITabGroup, UIToast 제거

### Added
- RecycleScrollView — 무한 재사용 스크롤 (Vertical/Horizontal, Grid, Cell/Page 스냅)
- IScrollCell 인터페이스

### Changed
- 패키지 정체성 변경: "UI Framework" → "UI Toolkit" (순수 도구 모음)
- asmdef에서 VContainer 참조 제거
- DI/, Popup/ 폴더 제거

## [1.0.1] - 2026-03-22

### Added
- UIStateBinder, ButtonClickEffect, NumberCounter, SafeAreaFitter
- UIFlyEffect, UIFollowWorld, UIProgressBar, UIShake
- UITabGroup, UIToast, UITutorialMask
- UIPopup, UIManager, UITransition, UIDialog
- UILifetimeScope (VContainer DI)
- UIStateBinderEditor (커스텀 인스펙터)
