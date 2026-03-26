#import <Foundation/Foundation.h>
#import <Security/Security.h>

static NSString* ServiceName = @"com.tjdtjq5.gameserver";

extern "C" {

void _KeychainSet(const char* key, const char* value) {
    NSString *nsKey = [NSString stringWithUTF8String:key];
    NSData *data = [[NSString stringWithUTF8String:value] dataUsingEncoding:NSUTF8StringEncoding];

    // 기존 삭제 후 추가
    NSDictionary *query = @{
        (__bridge id)kSecClass: (__bridge id)kSecClassGenericPassword,
        (__bridge id)kSecAttrService: ServiceName,
        (__bridge id)kSecAttrAccount: nsKey
    };
    SecItemDelete((__bridge CFDictionaryRef)query);

    NSDictionary *addQuery = @{
        (__bridge id)kSecClass: (__bridge id)kSecClassGenericPassword,
        (__bridge id)kSecAttrService: ServiceName,
        (__bridge id)kSecAttrAccount: nsKey,
        (__bridge id)kSecValueData: data,
        (__bridge id)kSecAttrAccessible: (__bridge id)kSecAttrAccessibleAfterFirstUnlock
    };
    SecItemAdd((__bridge CFDictionaryRef)addQuery, NULL);
}

const char* _KeychainGet(const char* key) {
    NSString *nsKey = [NSString stringWithUTF8String:key];
    NSDictionary *query = @{
        (__bridge id)kSecClass: (__bridge id)kSecClassGenericPassword,
        (__bridge id)kSecAttrService: ServiceName,
        (__bridge id)kSecAttrAccount: nsKey,
        (__bridge id)kSecReturnData: @YES,
        (__bridge id)kSecMatchLimit: (__bridge id)kSecMatchLimitOne
    };

    CFDataRef result = NULL;
    OSStatus status = SecItemCopyMatching((__bridge CFDictionaryRef)query, (CFTypeRef*)&result);

    if (status != errSecSuccess || result == NULL) return NULL;

    NSString *value = [[NSString alloc] initWithData:(__bridge NSData*)result encoding:NSUTF8StringEncoding];
    CFRelease(result);

    // Unity가 strdup된 문자열을 기대
    return strdup([value UTF8String]);
}

void _KeychainDelete(const char* key) {
    NSString *nsKey = [NSString stringWithUTF8String:key];
    NSDictionary *query = @{
        (__bridge id)kSecClass: (__bridge id)kSecClassGenericPassword,
        (__bridge id)kSecAttrService: ServiceName,
        (__bridge id)kSecAttrAccount: nsKey
    };
    SecItemDelete((__bridge CFDictionaryRef)query);
}

}
