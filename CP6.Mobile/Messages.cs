using CommunityToolkit.Mvvm.Messaging.Messages;

namespace CP6.Mobile;

public sealed class SsoCallbackMessage(Uri value) : ValueChangedMessage<Uri>(value);
public sealed class ScanBroadcastMessage(string value) : ValueChangedMessage<string>(value);
