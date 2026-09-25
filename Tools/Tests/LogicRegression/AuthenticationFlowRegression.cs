using System;
using BattlePvp.Networking;

internal static class AuthenticationFlowRegression
{
    internal static void Run(Action<bool, string> require)
    {
        var state = new AuthenticationAttemptState();
        require(!state.IsBusy && state.Current == null, "Authentication starts idle.");
        AuthenticationAttempt a = state.Begin(AuthenticationOperation.Login, " Alice ", 0);
        require(a.Username == "Alice" && a.Stage == AuthenticationStage.Response && state.IsBusy, "A login owns its normalized input and response stage.");
        require(state.Begin(AuthenticationOperation.Login, "Alice", 1) == null, "Repeated clicks must not issue a second current login.");
        AuthenticationAttempt b = state.Begin(AuthenticationOperation.Login, "Bob", 1);
        require(b.Id > a.Id && !state.ResolveResponse(a, true, 2), "A late A response must not approve B or restore A.");
        require(state.ResolveResponse(b, true, 2) && b.Stage == AuthenticationStage.Profile, "Only B may enter profile loading.");
        require(!state.ResolveResponse(b, true, 2.1), "Duplicate credential responses must not reset the profile session.");
        require(!state.ResolveProfile(a, true, 3), "A late profile completion cannot approve B.");
        require(state.ResolveProfile(b, true, 3) && b.Stage == AuthenticationStage.Succeeded, "The selected account may complete login.");
        require(!state.IsBusy && !state.ResolveProfile(b, true, 4), "Login completion is emitted once.");

        a = state.Begin(AuthenticationOperation.Login, "Alice", 5);
        b = state.Begin(AuthenticationOperation.Register, "Bob", 6);
        require(!state.ResolveResponse(a, true, 7), "Register supersedes an earlier login without adopting its credentials.");
        require(state.ResolveResponse(b, true, 7) && b.Stage == AuthenticationStage.Succeeded, "Registration has no profile-loading stage.");
        require(!state.ResolveProfile(b, true, 8), "Registration cannot become a login through a profile callback.");
        a = state.Begin(AuthenticationOperation.Register, "Alice", 9);
        b = state.Begin(AuthenticationOperation.Login, "Bob", 10);
        require(!state.ResolveResponse(a, true, 11), "A late registration cannot replace a newer login.");
        require(state.ResolveResponse(b, false, 11) && b.Stage == AuthenticationStage.Failed, "A confirmed login failure releases the UI.");
        a = state.Begin(AuthenticationOperation.Login, "Bob", 12);
        require(a != null && state.IsBusy, "A failed request must allow retry with the same username.");
        require(state.Expire(26.999, false) == null, "A response remains eligible just before its deadline.");
        require(!state.ResolveResponse(a, true, 27), "A callback at the deadline must fail even before Update.");
        require(state.Expire(27, false) == a && a.Stage == AuthenticationStage.Failed, "The response deadline completes failure once.");
        require(state.Expire(28, false) == null && !state.ResolveResponse(a, true, 28), "Late SDK completion cannot revive a timed-out request.");

        a = state.Begin(AuthenticationOperation.Login, "Alice", 30);
        require(state.ResolveResponse(a, true, 31), "The retry may authenticate.");
        require(state.ResolveProfile(a, false, 32) && a.Stage == AuthenticationStage.Failed, "A profile session reset cannot report login success.");
        a = state.Begin(AuthenticationOperation.Login, "Alice", 40);
        require(state.ResolveResponse(a, true, 41), "The next selected account may load its profile.");
        require(state.Expire(56.999, true) == null, "The profile callback has its own finite deadline.");
        require(state.Expire(57, true) == a && a.Stage == AuthenticationStage.Succeeded, "A missing profile response preserves authentication and permits lobby retry.");
        require(!state.ResolveProfile(a, true, 58), "A late profile callback cannot issue another success.");
        a = state.Begin(AuthenticationOperation.Login, "Alice", 60);
        require(state.ResolveResponse(a, true, 61), "A profile session can be replaced while waiting.");
        require(state.Expire(77, false) == a && a.Stage == AuthenticationStage.Failed, "A reset profile session never succeeds at timeout.");
        foreach (AuthenticationOperation operation in new[] { AuthenticationOperation.Login, AuthenticationOperation.Register })
        {
            a = state.Begin(operation, "Alice", 80);
            state.Cancel();
            require(!state.IsBusy && a.Stage == AuthenticationStage.Cancelled, "Screen exit releases the pending authentication state.");
            require(!state.ResolveResponse(a, true, 81) && state.Expire(100, true) == null, "Cancelled SDK requests never complete the current screen.");
        }
        a = state.Begin(AuthenticationOperation.Login, "Alice", 110);
        state.ResolveResponse(a, true, 111);
        state.Cancel();
        require(!state.ResolveProfile(a, true, 112), "Screen exit also invalidates a pending profile completion.");
        require(state.Begin(AuthenticationOperation.Login, "Alice", 113) != null, "Reopening the screen allows a fresh request.");
        require(state.Expire(double.NaN, true) == null, "Non-finite clock input cannot approve or expire a request.");

        var navigation = new LoginNavigationGate();
        require(!navigation.Schedule(1, 0), "An inactive screen cannot schedule a scene change.");
        navigation.Activate();
        require(navigation.Schedule(1, 0), "A visible screen may schedule the approved login.");
        require(!navigation.Schedule(1, 0) && !navigation.Schedule(2, 0), "Duplicate success callbacks schedule only one scene change.");
        require(!navigation.TryConsume(1, 1.199), "Navigation preserves the success-message delay.");
        require(navigation.TryConsume(1, 1.2) && !navigation.TryConsume(1, 2), "An approved login changes scene once.");
        navigation.Activate();
        navigation.Schedule(2, 3);
        navigation.Deactivate();
        require(!navigation.TryConsume(2, 5), "Closing the screen cancels scheduled navigation.");
        navigation.Activate();
        navigation.Schedule(2, 6);
        require(!navigation.TryConsume(3, 8) && !navigation.IsPending, "A newer authentication invalidates an older scheduled scene change.");
        require(navigation.Schedule(3, 8) && navigation.TryConsume(3, 10), "The latest approved login may navigate after invalidation.");
        navigation.Activate();
        require(!navigation.Schedule(0, 0) && !navigation.Schedule(1, double.NaN), "Navigation requires a valid approval and clock.");
    }
}
