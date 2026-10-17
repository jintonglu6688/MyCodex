using System.Runtime.Serialization;

namespace MyCodex.Domain.Entities
{
    [DataContract]
    public sealed class RemoteUnixSshProfileConfigSnapshot
    {
        private string _id;
        private string _name;
        private RemoteMacSshConfigSnapshot _ssh = new RemoteMacSshConfigSnapshot();

        [DataMember]
        public string Id
        {
            get { return string.IsNullOrWhiteSpace(_id) ? null : _id; }
            set { _id = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); }
        }

        [DataMember]
        public string Name
        {
            get { return string.IsNullOrWhiteSpace(_name) ? null : _name; }
            set { _name = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); }
        }

        [DataMember]
        public RemoteMacSshConfigSnapshot Ssh
        {
            get
            {
                if (_ssh == null)
                {
                    _ssh = new RemoteMacSshConfigSnapshot();
                }

                return _ssh;
            }
            set { _ssh = value ?? new RemoteMacSshConfigSnapshot(); }
        }
    }
}
