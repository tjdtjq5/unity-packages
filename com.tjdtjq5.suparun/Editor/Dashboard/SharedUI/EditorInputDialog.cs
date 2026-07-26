using UnityEditor;
using UnityEngine;

namespace Tjdtjq5.SupaRun.Editor
{
    /// <summary>
    /// 한 줄 입력을 받는 모달. `EditorUtility.DisplayDialog` 에는 입력칸이 없어서 만든 것이다.
    ///
    /// 쓰이는 자리는 **되돌릴 수 없는 동작의 확인**이다 — 프로젝트 삭제처럼 오클릭 비용이 큰 곳에서
    /// 이름을 손으로 치게 한다. 스냅샷 복원의 라벨 타이핑과 같은 장치다.
    /// </summary>
    public class EditorInputDialog : EditorWindow
    {
        string _message;
        string _text = "";
        string _okLabel = "확인";
        bool _accepted;
        bool _closed;

        /// <summary>모달을 띄우고 입력값을 돌려준다. 취소하면 null.</summary>
        public static string Show(string title, string message, string initial, string okLabel = "확인")
        {
            var w = CreateInstance<EditorInputDialog>();
            w.titleContent = new GUIContent(title);
            w._message = message;
            w._text = initial ?? "";
            w._okLabel = okLabel;

            // 마우스 근처에 띄운다 — 멀티 모니터에서 화면 중앙 계산은 자주 엉뚱한 곳을 잡는다.
            var size = new Vector2(420, 140);
            var pos = GUIUtility.GUIToScreenPoint(Event.current?.mousePosition ?? Vector2.zero);
            w.position = new Rect(pos.x - size.x / 2, pos.y - size.y / 2, size.x, size.y);
            w.minSize = w.maxSize = size;

            w.ShowModalUtility();   // 닫힐 때까지 여기서 멈춘다
            return w._accepted ? w._text : null;
        }

        void OnGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField(_message, EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(4);

            GUI.SetNextControlName("input");
            _text = EditorGUILayout.TextField(_text);
            EditorGUI.FocusTextInControl("input");

            // Enter/Esc 를 버튼과 같게 다룬다 — 타이핑하다 손을 옮기지 않게.
            var e = Event.current;
            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode is KeyCode.Return or KeyCode.KeypadEnter) { Accept(); e.Use(); }
                else if (e.keyCode == KeyCode.Escape) { Cancel(); e.Use(); }
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("취소", GUILayout.Width(80))) Cancel();
            if (GUILayout.Button(_okLabel, GUILayout.Width(80))) Accept();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(6);
        }

        void Accept()
        {
            if (_closed) return;
            _accepted = true; _closed = true;
            Close();
        }

        void Cancel()
        {
            if (_closed) return;
            _accepted = false; _closed = true;
            Close();
        }
    }
}
