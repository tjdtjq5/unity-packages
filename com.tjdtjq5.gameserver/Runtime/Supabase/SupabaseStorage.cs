namespace Tjdtjq5.GameServer.Supabase
{
    /// <summary>Supabase Storage. 파일 업로드/다운로드.</summary>
    public class SupabaseStorage
    {
        readonly SupabaseClient _client;

        internal SupabaseStorage(SupabaseClient client) => _client = client;

        // TODO: Phase 3에서 구현
        // - Upload(bucket, path, bytes)
        // - Download(bucket, path)
        // - GetPublicUrl(bucket, path)
        // - Delete(bucket, path)
    }
}
