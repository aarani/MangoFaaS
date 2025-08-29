using System.Diagnostics;

namespace MangoFaaS.Common;

public class Instrumentation
{
    private readonly ActivitySource _activitySource = new("MangoFaaS");

    public Activity? StartActivity(string name)
    {
        return _activitySource.StartActivity(name);
    }

}