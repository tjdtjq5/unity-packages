/**
 * vite 가 처리하는 에셋 import 의 타입 선언.
 *
 * CSS 를 부작용으로만 들여올 때(`import './x.css'`) tsc 가 모듈을 못 찾는다고 막는다 —
 * 실제로 번들러가 처리하는 것이라 타입은 빈 선언으로 충분하다.
 */
declare module '*.css'
declare module '*.svg'
declare module '*.png'
