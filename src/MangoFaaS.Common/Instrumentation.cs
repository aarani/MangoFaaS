using System.Diagnostics;

namespace MangoFaaS.Common;

public class Instrumentation
{
    public ActivitySource activitySource = new("MangoFaaS");

    public Activity? StartActivity(string name)
    {
        return activitySource.StartActivity(name);
    }

}