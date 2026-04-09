#nullable enable
using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// 플랫폼별 보안 세션 저장소.
    /// - iOS: Keychain (네이티브 플러그인 __Internal)
    /// - Android: KeyStore AES/GCM 암호화 → PlayerPrefs에 Base64 암호문
    /// - PC/Editor: PlayerPrefs (평문 fallback, 개발용)
    ///
    /// 키 prefix를 지원하여 MPPM Virtual Player 같은 멀티 인스턴스 환경에서
    /// 인스턴스마다 별도 세션을 보관할 수 있다.
    /// </summary>
    public class SecureSessionStorage : ISessionStorage
    {
        readonly string _keyPrefix;

#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidJavaObject _keyStore;
        bool _androidInitialized;
        const string ALIAS = "SupaRunSecureKey";
        const string ANDROID_PREFS = "SupaRun_Secure";
#endif

        /// <summary>
        /// 새 보안 저장소 생성.
        /// </summary>
        /// <param name="keyPrefix">
        /// 키 앞에 붙일 prefix. 빈 문자열이면 prefix 없음 (기본).
        /// MPPM Virtual Player 자동 분리 시 인스턴스 ID(예: "mppm40870be5")가 전달된다.
        /// </param>
        public SecureSessionStorage(string keyPrefix = "")
        {
            _keyPrefix = string.IsNullOrEmpty(keyPrefix) ? "" : keyPrefix + "_";
        }

        string K(string key) => _keyPrefix + key;

        public void Set(string key, string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                Delete(key);
                return;
            }

#if UNITY_IOS && !UNITY_EDITOR
            _KeychainSet(K(key), value);
#elif UNITY_ANDROID && !UNITY_EDITOR
            AndroidSet(K(key), value);
#else
            PlayerPrefs.SetString(K(key), value);
#endif
        }

        public string? Get(string key, string? defaultValue = "")
        {
#if UNITY_IOS && !UNITY_EDITOR
            var result = _KeychainGet(K(key));
            return result ?? defaultValue;
#elif UNITY_ANDROID && !UNITY_EDITOR
            return AndroidGet(K(key), defaultValue);
#else
            return PlayerPrefs.GetString(K(key), defaultValue);
#endif
        }

        public int GetInt(string key, int defaultValue = 0)
        {
            var str = Get(key, null);
            return str != null && int.TryParse(str, out var val) ? val : defaultValue;
        }

        public void SetInt(string key, int value)
        {
            Set(key, value.ToString());
        }

        public void Delete(string key)
        {
#if UNITY_IOS && !UNITY_EDITOR
            _KeychainDelete(K(key));
#elif UNITY_ANDROID && !UNITY_EDITOR
            AndroidDelete(K(key));
#else
            PlayerPrefs.DeleteKey(K(key));
#endif
        }

        public void Save()
        {
#if !UNITY_IOS || UNITY_EDITOR
#if !UNITY_ANDROID || UNITY_EDITOR
            PlayerPrefs.Save();
#endif
#endif
        }

        // ── iOS Keychain ──

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern void _KeychainSet(string key, string value);

        [DllImport("__Internal")]
        static extern string _KeychainGet(string key);

        [DllImport("__Internal")]
        static extern void _KeychainDelete(string key);
#endif

        // ── Android KeyStore ──

#if UNITY_ANDROID && !UNITY_EDITOR
        void AndroidInit()
        {
            if (_androidInitialized) return;
            try
            {
                // KeyStore 로드
                _keyStore = new AndroidJavaObject("java.security.KeyStore");
                using var ksClass = new AndroidJavaClass("java.security.KeyStore");
                _keyStore = ksClass.CallStatic<AndroidJavaObject>("getInstance", "AndroidKeyStore");
                _keyStore.Call("load", (AndroidJavaObject)null);

                // 키가 없으면 생성
                if (!_keyStore.Call<bool>("containsAlias", ALIAS))
                {
                    using var kgClass = new AndroidJavaClass("javax.crypto.KeyGenerator");
                    using var kg = kgClass.CallStatic<AndroidJavaObject>("getInstance", "AES", "AndroidKeyStore");
                    using var specBuilder = new AndroidJavaObject(
                        "android.security.keystore.KeyGenParameterSpec$Builder",
                        ALIAS,
                        3); // PURPOSE_ENCRYPT | PURPOSE_DECRYPT
                    specBuilder.Call<AndroidJavaObject>("setBlockModes", new object[] { "GCM" });
                    specBuilder.Call<AndroidJavaObject>("setEncryptionPaddings", new object[] { "NoPadding" });
                    using var spec = specBuilder.Call<AndroidJavaObject>("build");
                    kg.Call("init", spec);
                    kg.Call<AndroidJavaObject>("generateKey");
                }

                _androidInitialized = true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SupaRun:SessionStorage] Android KeyStore 초기화 실패, PlayerPrefs fallback: {ex.Message}");
                _androidInitialized = false;
            }
        }

        void AndroidSet(string key, string value)
        {
            AndroidInit();
            if (!_androidInitialized)
            {
                PlayerPrefs.SetString(key, value);
                return;
            }

            try
            {
                // AES/GCM 암호화
                using var cipherClass = new AndroidJavaClass("javax.crypto.Cipher");
                using var cipher = cipherClass.CallStatic<AndroidJavaObject>("getInstance", "AES/GCM/NoPadding");

                var entry = _keyStore.Call<AndroidJavaObject>("getEntry", ALIAS, null);
                var secretKey = entry.Call<AndroidJavaObject>("getSecretKey");
                cipher.Call("init", 1, secretKey); // ENCRYPT_MODE

                var plainBytes = System.Text.Encoding.UTF8.GetBytes(value);
                var encryptedBytes = cipher.Call<byte[]>("doFinal", plainBytes);
                var ivBytes = cipher.Call<AndroidJavaObject>("getIV").Call<byte[]>("clone");

                // IV + 암호문을 Base64로 저장
                var combined = new byte[ivBytes.Length + encryptedBytes.Length];
                Array.Copy(ivBytes, 0, combined, 0, ivBytes.Length);
                Array.Copy(encryptedBytes, 0, combined, ivBytes.Length, encryptedBytes.Length);

                PlayerPrefs.SetString(ANDROID_PREFS + "_" + key, Convert.ToBase64String(combined));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SupaRun:SessionStorage] 암호화 실패, 평문 저장: {ex.Message}");
                PlayerPrefs.SetString(key, value);
            }
        }

        string AndroidGet(string key, string defaultValue)
        {
            AndroidInit();
            var secureKey = ANDROID_PREFS + "_" + key;

            if (!_androidInitialized || !PlayerPrefs.HasKey(secureKey))
            {
                // fallback: 이전 평문 데이터 마이그레이션
                if (PlayerPrefs.HasKey(key))
                    return PlayerPrefs.GetString(key, defaultValue);
                return defaultValue;
            }

            try
            {
                var combined = Convert.FromBase64String(PlayerPrefs.GetString(secureKey));
                // GCM IV = 12 bytes
                var iv = new byte[12];
                var encrypted = new byte[combined.Length - 12];
                Array.Copy(combined, 0, iv, 0, 12);
                Array.Copy(combined, 12, encrypted, 0, encrypted.Length);

                using var cipherClass = new AndroidJavaClass("javax.crypto.Cipher");
                using var cipher = cipherClass.CallStatic<AndroidJavaObject>("getInstance", "AES/GCM/NoPadding");
                using var gcmSpec = new AndroidJavaObject("javax.crypto.spec.GCMParameterSpec", 128, iv);

                var entry = _keyStore.Call<AndroidJavaObject>("getEntry", ALIAS, null);
                var secretKey = entry.Call<AndroidJavaObject>("getSecretKey");
                cipher.Call("init", 2, secretKey, gcmSpec); // DECRYPT_MODE

                var decrypted = cipher.Call<byte[]>("doFinal", encrypted);
                return System.Text.Encoding.UTF8.GetString(decrypted);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SupaRun:SessionStorage] 복호화 실패: {ex.Message}");
                return defaultValue;
            }
        }

        void AndroidDelete(string key)
        {
            PlayerPrefs.DeleteKey(ANDROID_PREFS + "_" + key);
            PlayerPrefs.DeleteKey(key); // 평문 fallback도 삭제
        }
#endif
    }
}
