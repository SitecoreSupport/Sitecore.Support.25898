using Sitecore.Abstractions;
using Sitecore.Eventing;
using Sitecore.Eventing.Remote;
using Sitecore.Events;
using Sitecore.Framework.Publishing.Eventing.Remote;
using Sitecore.Framework.Publishing.PublishJobQueue;
using Sitecore.Publishing.Service.SitecoreAbstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using Sitecore.Globalization;
using Sitecore.Data.Managers;
using Sitecore.Publishing.Service.Events;
using Sitecore.Publishing;

namespace Sitecore.Support.Publishing.Service.Events
{
    public class PublishEndHandler : PublishEventHandlerBase
    {
        private const string PublishEndEventName = "publish:end";

        private const string PublishFailEventName = "publish:fail";

        private const string PublishCompleteEventName = "publish:complete";

        private readonly PublishingLogWrapper _logger;

        public PublishEndHandler() : base(new EventWrapper(), new DatabaseFactoryWrapper(new PublishingLogWrapper()))
        {
            this._logger = new PublishingLogWrapper();
        }

        public PublishEndHandler(IEvent eventing, IDatabaseFactory factory) : base(eventing, factory)
        {
            this._logger = new PublishingLogWrapper();
        }

        public void TriggerPublishEnd(object sender, EventArgs args)
        {
            SitecoreEventArgs sitecoreEventArgs = args as SitecoreEventArgs;
            if (sitecoreEventArgs == null || sitecoreEventArgs.Parameters == null || !sitecoreEventArgs.Parameters.Any<object>())
            {
                return;
            }
            PublishingJobEndEventArgs publishingJobEndEventArgs = sitecoreEventArgs.Parameters[0] as PublishingJobEndEventArgs;
            if (publishingJobEndEventArgs == null || publishingJobEndEventArgs.EventData == null)
            {
                return;
            }
            PublishingJobEndEvent eventData = publishingJobEndEventArgs.EventData;
            List<Sitecore.Publishing.PublishOptions> list = new List<Sitecore.Publishing.PublishOptions>();
            PublishingJobTargetMetadata[] targets = eventData.Targets;
            for (int i = 0; i < targets.Length; i++)
            {
                PublishingJobTargetMetadata publishingJobTargetMetadata = targets[i];
                Publisher publisher = base.BuildPublisher(eventData, publishingJobTargetMetadata.TargetDatabaseName, publishingJobTargetMetadata.TargetId);
                if (!publishingJobTargetMetadata.Succeeded)
                {
                    this._eventing.RaiseEvent("publish:fail", new object[]
                    {
                        publisher,
                        new Exception(string.Concat(new string[]
                        {
                            "Publish job for target: ",
                            publishingJobTargetMetadata.TargetName,
                            " (Database:",
                            publishingJobTargetMetadata.TargetDatabaseName,
                            ") failed. See Publishing Service log for more information."
                        }))
                    });
                }
                else
                {
                    foreach (string lang in eventData.LanguageNames)
                    {
                        Sitecore.Publishing.PublishOptions opt = publisher.Options.Clone();
                        opt.Language = LanguageManager.GetLanguage(lang);
                        list.Add(opt);
                    }

                    this._logger.Info("Raising : 'publish:end' for '" + publishingJobTargetMetadata.TargetName + "'", null);
                    this._eventing.RaiseEvent("publish:end", new object[]
                    {
                        publisher
                    });
                    publisher.Options.TargetDatabase.RemoteEvents.Queue.QueueEvent<PublishEndRemoteEvent>(new PublishEndRemoteEvent(publisher));
                }
            }
            bool flag = eventData.Status == PublishJobStatus.Complete;
            
            IEnumerable<DistributedPublishOptions> enumerable = from option in list
                                                                select new DistributedPublishOptions(option);
            List<Language> langs = new List<Language>();
            foreach (string langName in publishingJobEndEventArgs.EventData.LanguageNames)
            {
                langs.Add(LanguageManager.GetLanguage(langName));
            }
            this._logger.Info("Raising : 'publish:complete'", null);
            Event.RaiseEvent("publish:complete", new object[]
            {
                enumerable,
                0,
                flag,
                langs
            });
            EventManager.QueueEvent<PublishCompletedRemoteEvent>(new PublishCompletedRemoteEvent(enumerable, 0L, flag));
            if (!flag && eventData.Targets.Length == 0)
            {
                this._eventing.RaiseEvent("publish:fail", new object[]
                {
                    null,
                    new Exception("Publish job : " + eventData.JobId + " failed. No Targets found. See Publishing Service log for more information.")
                });
            }
        }
    }
}
