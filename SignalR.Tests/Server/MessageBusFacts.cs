﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SignalR.Tests.Infrastructure;
using Xunit;

namespace SignalR.Tests.Server
{
    public class MessageBusFacts
    {
        [Fact]
        public void NewSubscriptionGetsAllMessages()
        {
            var dr = new DefaultDependencyResolver();
            var bus = new MessageBus(dr);
            var subscriber = new Subscriber(new[] { "key" });
            var wh = new ManualResetEventSlim(initialState: false);
            IDisposable subscription = null;

            try
            {
                bus.Publish("test", "key", "1").Wait();

                subscription = bus.Subscribe(subscriber, null, result =>
                {
                    if (!result.Terminal)
                    {
                        var m = EnumerateMessages(result).Single();

                        Assert.Equal("key", m.Key);
                        Assert.Equal("value", m.Value);

                        wh.Set();

                        return TaskAsyncHelper.True;
                    }

                    return TaskAsyncHelper.False;

                }, 10);

                bus.Publish("test", "key", "value").Wait();

                Assert.True(wh.Wait(TimeSpan.FromSeconds(5)));
            }
            finally
            {
                if (subscription != null)
                {
                    subscription.Dispose();
                }
            }
        }

        [Fact]
        public void SubscriptionWithExistingCursor()
        {
            var dr = new DefaultDependencyResolver();
            var bus = new MessageBus(dr);
            var subscriber = new Subscriber(new[] { "key" });
            var cd = new CountDownRange<int>(Enumerable.Range(2, 4));
            IDisposable subscription = null;

            bus.Publish("test", "key", "1").Wait();
            bus.Publish("test", "key", "2").Wait();
            bus.Publish("test", "key", "3").Wait();
            bus.Publish("test", "key", "4").Wait();

            try
            {
                subscription = bus.Subscribe(subscriber, "key,00000001", result =>
                {
                    foreach (var m in EnumerateMessages(result))
                    {
                        int n = Int32.Parse(m.Value);
                        Assert.True(cd.Mark(n));
                    }

                    return TaskAsyncHelper.True;

                }, 10);

                bus.Publish("test", "key", "5");

                Assert.True(cd.Wait(TimeSpan.FromSeconds(5)));
            }
            finally
            {
                if (subscription != null)
                {
                    subscription.Dispose();
                }
            }
        }

        [Fact]
        public void SubscriptionWithMultipleExistingCursors()
        {
            var dr = new DefaultDependencyResolver();
            var bus = new MessageBus(dr);
            var subscriber = new Subscriber(new[] { "key", "key2" });
            var cdKey = new CountDownRange<int>(Enumerable.Range(2, 4));
            var cdKey2 = new CountDownRange<int>(new[] { 1, 2, 10 });
            IDisposable subscription = null;

            bus.Publish("test", "key", "1").Wait();
            bus.Publish("test", "key", "2").Wait();
            bus.Publish("test", "key", "3").Wait();
            bus.Publish("test", "key", "4").Wait();
            bus.Publish("test", "key2", "1").Wait();
            bus.Publish("test", "key2", "2").Wait();

            try
            {
                subscription = bus.Subscribe(subscriber, "key,00000001|key2,00000000", result =>
                {
                    foreach (var m in EnumerateMessages(result))
                    {
                        int n = Int32.Parse(m.Value);
                        if (m.Key == "key")
                        {
                            Assert.True(cdKey.Mark(n));
                        }
                        else
                        {
                            Assert.True(cdKey2.Mark(n));
                        }
                    }

                    return TaskAsyncHelper.True;

                }, 10);

                bus.Publish("test", "key", "5");
                bus.Publish("test", "key2", "10");

                Assert.True(cdKey.Wait(TimeSpan.FromSeconds(5)));
                Assert.True(cdKey2.Wait(TimeSpan.FromSeconds(5)));
            }
            finally
            {
                if (subscription != null)
                {
                    subscription.Dispose();
                }
            }
        }

        [Fact]
        public void AddingEventAndSendingMessages()
        {
            var dr = new DefaultDependencyResolver();
            var bus = new MessageBus(dr);
            var subscriber = new Subscriber(new[] { "a" });
            int max = 100;
            var cd = new CountDownRange<int>(Enumerable.Range(0, max));
            int prev = -1;
            IDisposable subscription = null;

            try
            {
                subscription = bus.Subscribe(subscriber, null, result =>
                {
                    foreach (var m in EnumerateMessages(result))
                    {
                        int n = Int32.Parse(m.Value);
                        Assert.True(prev < n, "out of order");
                        prev = n;
                        Assert.True(cd.Mark(n));
                    }

                    return TaskAsyncHelper.True;
                }, 10);

                for (int i = 0; i < max; i++)
                {
                    subscriber.AddEvent("b");
                    bus.Publish("test", "b", i.ToString()).Wait();
                }

                Assert.True(cd.Wait(TimeSpan.FromSeconds(10)));
            }
            finally
            {
                if (subscription != null)
                {
                    subscription.Dispose();
                }
            }
        }

        private static IEnumerable<Message> EnumerateMessages(MessageResult result)
        {
            for (int i = 0; i < result.Messages.Count; i++)
            {
                for (int j = result.Messages[i].Offset; j < result.Messages[i].Offset + result.Messages[i].Count; j++)
                {
                    Message message = result.Messages[i].Array[j];
                    yield return message;
                }
            }
        }

        private class Subscriber : ISubscriber
        {
            public Subscriber(IEnumerable<string> keys)
            {
                EventKeys = keys;
                Identity = Guid.NewGuid().ToString();
            }

            public IEnumerable<string> EventKeys { get; private set; }

            public string Identity { get; private set; }

            public event Action<string, string> EventAdded;

            public event Action<string> EventRemoved;

            public void AddEvent(string eventName, string cursor = "0")
            {
                if (EventAdded != null)
                {
                    EventAdded(eventName, cursor);
                }
            }

            public void RemoveEvent(string eventName)
            {
                if (EventRemoved != null)
                {
                    EventRemoved(eventName);
                }
            }
        }
    }
}
