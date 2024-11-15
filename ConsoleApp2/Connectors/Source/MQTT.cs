using System.Text;
using MQTTnet;

namespace ConsoleApp2.Connectors.Source;

public class MQTT: ISource
{
    private PropertyBag _configuration = null;
    private List<PropertyBag> _readItems = null;
    
    public PropertyBag ConnectorConfiguration { get { return _configuration; } }
    public List<PropertyBag> ItemsConfiguration { get { return _readItems; } }
    
    private IMqttClient _client = null;
    private Dictionary<string, PropertyBag> _incomingBuffer = null;
    private object _collectionLock = null;
    
    public void Initialize(PropertyBag configuration, List<PropertyBag> readItems)
    {
        _configuration = configuration;
        _readItems = readItems;
        _incomingBuffer = new Dictionary<string, PropertyBag>();
        _collectionLock = new object();
        
        _configuration.MakeDefaultProperty("enabled", true);
        _configuration.MakeDefaultProperty("address", "127.0.0.1");
        _configuration.MakeDefaultProperty("port", 1883);
        _configuration.MakeDefaultProperty("username", string.Empty);
        _configuration.MakeDefaultProperty("password", string.Empty);
        
        _configuration.MakeDefaultProperty("client_id", Guid.NewGuid().ToString());
        _configuration.MakeDefaultProperty("clean_session", true);
        
    }

    public void Create()
    {
        if (!_configuration.GetProperty<bool>("enabled"))
        {
            return;
        }
        
        _client = new MqttClientFactory().CreateMqttClient();
        _client.DisconnectedAsync += ClientOnDisconnectedAsync;
        _client.ApplicationMessageReceivedAsync += ClientOnApplicationMessageReceivedAsync;
    }

    public void Connect()
    {
        if (!_configuration.GetProperty<bool>("enabled"))
        {
            return;
        }
        
        var clientOptions = new MqttClientOptionsBuilder()
            .WithClientId(_configuration.GetProperty<string>("client_id"))
            .WithTcpServer(_configuration.GetProperty<string>("address"), _configuration.GetProperty<int>("port"))
            .WithCleanSession(_configuration.GetProperty<bool>("clean_session"));
        var options = clientOptions.Build();
        var result = _client.ConnectAsync(options).Result;
        //if (result.ResultCode != MqttClientConnectResultCode.Success)
        //    return false;
        var mqttSubscribeOptions = new MqttClientFactory().CreateSubscribeOptionsBuilder();
        foreach(var readItem in _readItems)
        {
            mqttSubscribeOptions.WithTopicFilter(f => { f.WithTopic(readItem.GetProperty<string>("address")); });
        }
        var subscribeResult = _client.SubscribeAsync(mqttSubscribeOptions.Build()).Result;
    }

    public void Disconnect()
    {
        if (!_configuration.GetProperty<bool>("enabled"))
        {
            return;
        }
        
        _client.DisconnectAsync().Wait();
    }

    public List<PropertyBag> Read()
    {
        if (!_configuration.GetProperty<bool>("enabled"))
        {
            return new List<PropertyBag>();
        }
        
        List<PropertyBag> items = new List<PropertyBag>();

        lock (_collectionLock)
        {
            foreach (var buffer in _incomingBuffer)
            {
                var item = new PropertyBag();
                item.SetProperty("address", buffer.Key);
                item.SetProperty("value", buffer.Value.GetProperty<object>("value"));
                item.SetProperty("timestamp", buffer.Value.GetProperty<DateTime>("timestamp"));
                items.Add(item);
            }
        }

        return items;
    }
    
    private Task ClientOnApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs arg)
    {
        lock (_collectionLock)
        {
            _incomingBuffer[arg.ApplicationMessage.Topic] = new PropertyBag();
            _incomingBuffer[arg.ApplicationMessage.Topic].SetProperty("value", arg.ApplicationMessage.ConvertPayloadToString());
            _incomingBuffer[arg.ApplicationMessage.Topic].SetProperty("timestamp", DateTime.Now);
        }

        return Task.FromResult(0);
    }

    private Task ClientOnDisconnectedAsync(MqttClientDisconnectedEventArgs arg)
    {
        return Task.FromResult(0);
    }
}