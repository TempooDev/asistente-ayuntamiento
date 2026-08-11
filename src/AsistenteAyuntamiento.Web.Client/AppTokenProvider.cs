using Microsoft.AspNetCore.Components;
using System;

namespace AsistenteAyuntamiento.Web.Client;

public class AppTokenProvider
{
    private string? _accessToken;
    private readonly PersistentComponentState _state;
    private bool _initialized;

    public AppTokenProvider(PersistentComponentState state)
    {
        _state = state;
    }

    public string? AccessToken 
    { 
        get
        {
            if (!_initialized)
            {
                _initialized = true;
                if (_state.TryTakeFromJson<string>("AccessToken", out var token))
                {
                    Console.WriteLine("AppTokenProvider: Token retrieved from PersistentComponentState.");
                    _accessToken = token;
                }
                else
                {
                    Console.WriteLine("AppTokenProvider: TryTakeFromJson failed or no token found.");
                }
            }
            return _accessToken;
        }
        set
        {
            Console.WriteLine("AppTokenProvider: Token set via setter.");
            _accessToken = value;
            _initialized = true;
        }
    }
}
