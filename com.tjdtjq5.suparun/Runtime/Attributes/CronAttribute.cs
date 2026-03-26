using System;

namespace Tjdtjq5.SupaRun
{
    /// <summary>
    /// [Service] 클래스 내에서 Cron 스케줄로 실행할 메서드를 지정한다.
    /// 배포 시 Cloud Scheduler 잡으로 자동 등록된다.
    /// 별칭: @daily, @weekly, @hourly, @every 30m
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class CronAttribute : Attribute
    {
        public string Expression { get; }
        public string TimeZone { get; }
        public string Description { get; }

        public CronAttribute(string expression, string timeZone = "Etc/UTC", string description = null)
        {
            Expression = expression;
            TimeZone = timeZone;
            Description = description;
        }
    }
}
