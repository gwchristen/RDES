using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RDES.App.Models
{
    public class DeviceRecord : INotifyPropertyChanged
    {
        private long _id;
        private string _serialNumber = string.Empty;
        private string _moduleNumber = string.Empty;
        private string _defect = string.Empty;
        private string _deviceCode = string.Empty;
        private string _manufacturerCode = string.Empty;
        private string _mfgDate = string.Empty;
        private string _modType = string.Empty;
        private string _modNumber = string.Empty;
        private string _problem = string.Empty;
        private string _otherProblem = string.Empty;
        private string _recordType = string.Empty;
        private string _catalog = string.Empty;
        private string _fileNumber = string.Empty;
        private string _status = "Pending";
        private string _opCo = "OH - RMA";
        private string _aclaraSerialStart = string.Empty;
        private string _aclaraSerialEnd = string.Empty;
        private string _customerSerialNumber = string.Empty;
        private string _materialGroup = string.Empty;
        private string _failureLocation = string.Empty;
        private string _customerIssue = string.Empty;
        private string _customerInput = string.Empty;
        private int _quantity = 1;
        private string _notes = string.Empty;
        private string _createdBy = string.Empty;
        private DateTime _createdAt = DateTime.Now;
        private string _updatedBy = string.Empty;
        private DateTime _updatedAt = DateTime.Now;
        private string _machineName = string.Empty;
        private bool _isSelected = false;

        public bool IsSelected
        {
            get => _isSelected;
            set => SetField(ref _isSelected, value);
        }

        public long Id
        {
            get => _id;
            set => SetField(ref _id, value);
        }

        public string SerialNumber
        {
            get => _serialNumber;
            set => SetField(ref _serialNumber, value?.Trim() ?? string.Empty);
        }

        public string ModuleNumber
        {
            get => _moduleNumber;
            set => SetField(ref _moduleNumber, value?.Trim() ?? string.Empty);
        }

        public string Defect
        {
            get => _defect;
            set => SetField(ref _defect, value?.Trim() ?? string.Empty);
        }

        public string DeviceCode
        {
            get => _deviceCode;
            set => SetField(ref _deviceCode, value?.Trim() ?? string.Empty);
        }

        public string ManufacturerCode
        {
            get => _manufacturerCode;
            set => SetField(ref _manufacturerCode, value?.Trim() ?? string.Empty);
        }

        public string MfgDate
        {
            get => _mfgDate;
            set => SetField(ref _mfgDate, value?.Trim() ?? string.Empty);
        }

        public string ModType
        {
            get => _modType;
            set => SetField(ref _modType, value?.Trim() ?? string.Empty);
        }

        public string ModNumber
        {
            get => _modNumber;
            set => SetField(ref _modNumber, value?.Trim() ?? string.Empty);
        }

        public string Problem
        {
            get => _problem;
            set => SetField(ref _problem, value?.Trim() ?? string.Empty);
        }

        public string OtherProblem
        {
            get => _otherProblem;
            set => SetField(ref _otherProblem, value?.Trim() ?? string.Empty);
        }

        public string RecordType
        {
            get => _recordType;
            set => SetField(ref _recordType, value?.Trim() ?? string.Empty);
        }

        public string Catalog
        {
            get => _catalog;
            set => SetField(ref _catalog, value?.Trim() ?? string.Empty);
        }

        public string FileNumber
        {
            get => _fileNumber;
            set => SetField(ref _fileNumber, value?.Trim() ?? string.Empty);
        }

        public string Status
        {
            get => _status;
            set => SetField(ref _status, value?.Trim() ?? "Pending");
        }

        public string OpCo
        {
            get => _opCo;
            set => SetField(ref _opCo, value?.Trim() ?? "OH - RMA");
        }

        public string AclaraSerialStart
        {
            get => _aclaraSerialStart;
            set => SetField(ref _aclaraSerialStart, value?.Trim() ?? string.Empty);
        }

        public string AclaraSerialEnd
        {
            get => _aclaraSerialEnd;
            set => SetField(ref _aclaraSerialEnd, value?.Trim() ?? string.Empty);
        }

        public string CustomerSerialNumber
        {
            get => _customerSerialNumber;
            set => SetField(ref _customerSerialNumber, value?.Trim() ?? string.Empty);
        }

        public string MaterialGroup
        {
            get => _materialGroup;
            set => SetField(ref _materialGroup, value?.Trim() ?? string.Empty);
        }

        public string FailureLocation
        {
            get => _failureLocation;
            set => SetField(ref _failureLocation, value?.Trim() ?? string.Empty);
        }

        public string CustomerIssue
        {
            get => _customerIssue;
            set => SetField(ref _customerIssue, value?.Trim() ?? string.Empty);
        }

        public string CustomerInput
        {
            get => _customerInput;
            set => SetField(ref _customerInput, value?.Trim() ?? string.Empty);
        }

        public int Quantity
        {
            get => _quantity;
            set => SetField(ref _quantity, value);
        }

        public string Notes
        {
            get => _notes;
            set => SetField(ref _notes, value?.Trim() ?? string.Empty);
        }

        public string CreatedBy
        {
            get => _createdBy;
            set => SetField(ref _createdBy, value?.Trim() ?? string.Empty);
        }

        public DateTime CreatedAt
        {
            get => _createdAt;
            set => SetField(ref _createdAt, value);
        }

        public string UpdatedBy
        {
            get => _updatedBy;
            set => SetField(ref _updatedBy, value?.Trim() ?? string.Empty);
        }

        public DateTime UpdatedAt
        {
            get => _updatedAt;
            set => SetField(ref _updatedAt, value);
        }

        public string MachineName
        {
            get => _machineName;
            set => SetField(ref _machineName, value?.Trim() ?? string.Empty);
        }

        public DeviceRecord Clone()
        {
            return (DeviceRecord)MemberwiseClone();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
