using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using k8s;
using k8s.Models;

namespace Akka.Cluster.Discovery.KubernetesApi
{
    public class KubernetesDiscoveryService : LocklessDiscoveryService
    {
        #region Internal classes

        /// <summary>
        /// Message scheduled by <see cref="KubernetesDiscoveryService"/> for itself. 
        /// Used to trigger periodic restart of k8s client.
        /// </summary>
        public sealed class RestartClient
        {
            public static RestartClient Instance { get; } = new RestartClient();

            private RestartClient()
            {
            }
        }

        #endregion

        private readonly KubernetesSettings settings;
        private IKubernetes k8s;
        private readonly ILoggingAdapter logger = Context.GetLogger();
        
        private readonly int tcpPort;
        private readonly string actorSystemName;

        public KubernetesDiscoveryService(Config config) 
            : this(new KubernetesSettings(config))
        {            
        }

        public KubernetesDiscoveryService(KubernetesSettings settings)
            : this(CreateKubernetesClient(settings), settings)
        {
        }

        public KubernetesDiscoveryService(IKubernetes kubernetesClient, KubernetesSettings settings) : base(settings)
        {
            this.tcpPort = ((ExtendedActorSystem)Context.System).Provider.DefaultAddress.Port ?? Context.System.Settings.Config.GetInt("akka.remote.dot-netty.tcp.port");
            this.actorSystemName = ((ExtendedActorSystem)Context.System).Provider.DefaultAddress.System;
            this.k8s = kubernetesClient;
            this.settings = settings;            
        }

        protected override void Ready()
        {
            base.Ready();
            Receive<RestartClient>(_ =>
            {
                Log.Debug("Restarting k8s client...");

                k8s.Dispose();
                k8s = CreateKubernetesClient(settings);
            });
        }


        private static IKubernetes CreateKubernetesClient(KubernetesSettings settings)
        {
            var k8sClientConfig = KubernetesClientConfiguration.InClusterConfig();
            k8sClientConfig.Namespace = settings.KubernetesNamespace;
            return new Kubernetes(k8sClientConfig);
        }

        protected override async Task<IEnumerable<Address>> GetNodesAsync(bool onlyAlive)
        {
            logger.Debug("Refreshing nodes from k8s...");
            try
            {
                var pods = await k8s.ListNamespacedPodAsync(settings.KubernetesNamespace, labelSelector: settings.LabelSelector);          
                
                logger.Debug($"{pods.Items.Count} pods found: {string.Join(", ", pods.Items.Select(pod => $"{pod.Status.PodIP}:{pod.Status.Phase} ({string.Join("; ", pod.Status.Conditions.Select(c => $"{c.Type}={c.Status} (Msg: {c.Message}, Reason: {c.Reason})"))})"))}");

                var nodes = pods.Items
                    .Where(pod => pod?.Status?.PodIP != null && (!onlyAlive || (ConditionsOK(pod.Status.Conditions) && StatusOK(pod.Status))))
                    .Select(pod => new Address("akka.tcp", actorSystemName, pod.Status.PodIP, this.tcpPort))
                    .ToList();

                nodes.Add(Cluster.SelfAddress);
                nodes = nodes.Distinct().ToList();

                logger.Debug($"{nodes.Count} nodes found: {string.Join(", ", nodes.Select(address => address.ToString()))}");

                return nodes;
            }
            catch (Exception error)
            {
                logger.Error(error, "Could not poll k8s API for nodes: {0}", error);
                throw;
            }
        }

        private bool ConditionsOK(IList<V1PodCondition> conditions)
        {
            return conditions.All(x => x.Status.Equals("True"));
        }

        private bool StatusOK(V1PodStatus status)
        {
            return status.Phase.Equals("Running");
        }

        protected override Task RegisterNodeAsync(MemberEntry node) => Task.CompletedTask;
        protected override Task DeregisterNodeAsync(MemberEntry node) => Task.CompletedTask;
        protected override Task MarkAsAliveAsync(MemberEntry node) => Task.CompletedTask;
    }
}
