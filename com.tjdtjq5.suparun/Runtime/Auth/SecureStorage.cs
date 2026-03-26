using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// 플랫폼별 보안 저장소.
    /// Android: KeyStore AES 암호화 → PlayerPrefs에 암호문 저장
    /// iOS: Keychain (네이티브 플러그인)
    /// PC/Editor: PlayerPrefs fallback
    /// </summary>
    public static class SecureStorage
    {
        public static void Set(string key, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                Delete(key);
                return;
            }

#if UNITY_IOS && !UNITY_EDITOR
            _KeychainSet(key, value);
#elif UNITY_ANDROID && !UNITY_EDITOR
            AndroidSet(key, value);
#else
            PlayerPrefs.SetString(key, value);
#endif
        }

        public static string Get(string key, string defaultValue = "")
        {
#if UNITY_IOS && !UNITY_EDITOR
            var result = _KeychainGet(key);
            return result ?? defaultValue;
#elif UNITY_ANDROID && !UNITY_EDITOR
            return AndroidGet(key, defaultValue);
#else
            return PlayerPrefs.GetString(key, defaultValue);
#endif
        }

        public static int GetInt(string key, int defaultValue = 0)
        {
            var str = Get(key, null);
            return str != null && int.TryParse(str, out var val) ? val : defaultValue;
        }

        public static void SetInt(string key, int value)
        {
            Set(key, value.ToString());
        }

        public static void Delete(string key)
        {
#if UNITY_IOS && !UNITY_EDITOR
            _KeychainDelete(key);
#elif UNITY_ANDROID && !UNITY_EDITOR
            AndroidDelete(key);
#else
            PlayerPrefs.DeleteKey(key);
#endif
        }

        public static void Save()
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
        static AndroidJavaObject _keyStore;
        static readonly string ALIAS = "SupaRunSecureKey";
        static readonly string ANDROID_PREFS = "SupaRun_Secure";
        static bool _androidInitialized;

        static void AndroidInit()
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
                Debug.LogWarning($"[SupaRun:SecureStorage] Android KeyStore 초기화 실패, PlayerPrefs fallback: {ex.Message}");
                _androidInitialized = false;
            }
        }

        static void AndroidSet(string key, string value)
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
                Debug.LogWarning($"[SupaRun:SecureStorage] 암호화 실패, 평문 저장: {ex.Message}");
                PlayerPrefs.SetString(key, value);
            }
        }

        static string AndroidGet(string key, string defaultValue)
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
                Debug.LogWarning($"[SupaRun:SecureStorage] 복호화 실패: {ex.Message}");
                return defaultValue;
            }
        }

        static void AndroidDelete(string key)
        {
            PlayerPrefs.DeleteKey(ANDROID_PREFS + "_" + key);
            PlayerPrefs.DeleteKey(key); // 평문 fallback도 삭제
        }
#endif
    }
}
