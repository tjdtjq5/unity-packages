# Feature Index

> 자동 생성 — /ft:scan 실행 시 갱신됨

| 경로 | 피처명 | 상태 | 주요 API |
|------|--------|------|----------|
| Runtime/Client | Client | stable | SupaRun.Get, GetAll, Initialize, Client, Auth, Realtime |
| Runtime/Auth | Auth | stable | SupabaseAuth.EnsureLoggedIn, SignOut, TryRefreshToken, OAuthHandler |
| Runtime/DB | DB | stable | IGameDB.Get, GetAll, Save, Delete, Query, Count, LocalGameDB |
| Runtime/Supabase | Supabase | stable | SupabaseRealtime, RealtimeChannel.Subscribe, SupabaseStorage |
| Runtime/Attributes | Attributes | stable | [Config], [Table], [Service], [Json], [PrimaryKey], [ForeignKey] 등 18종 |
| Editor/Deploy | Deploy | stable | DeployManager.Deploy, GenerateFiles, ServerCodeGenerator.Generate |
| Editor/Dashboard | Dashboard | stable | SupaRunDashboard (EditorWindow), SetupWizard, 5개 탭 |
| Editor/Features | Features | stable | FeatureInstaller.Install/Uninstall, FeatureRegistry, FeaturesWindow |
| Templates/AdminTemplate~ | Admin | stable | index.html SPA — Config CRUD, JSON 에디터, 관리자, 변경 이력 |
| Templates/AspNetTemplate~ | Server | stable | Program.cs, Dockerfile — JWT 인증, Rate Limiting, Auto Migration |
| SourceGen~ | SourceGen | stable | ServiceGenerator, TableQueryGenerator — [Service]/[Table] 프록시 생성 |
