import 'dart:async';
import 'package:signalr_netcore/signalr_client.dart';
import 'api_client.dart';

class PosRealtime {
  PosRealtime(this.api);
  final PosApiClient api;
  HubConnection? _hub;
  final events = StreamController<Map<String, dynamic>>.broadcast();

  Future<void> connect() async {
    final token = await api.token();
    if (token == null) return;
    final hub = HubConnectionBuilder()
        .withUrl('${api.baseUrl}/hubs/pos', options: HttpConnectionOptions(accessTokenFactory: () async => token))
        .build();
    _hub = hub;
    for (final name in ['order.updated', 'message.created', 'messages.read', 'typing.changed', 'call.signal', 'payment.updated']) {
      hub.on(name, (args) => events.add({'type': name, 'data': args?.isNotEmpty == true ? args!.first : null}));
    }
    await hub.start();
  }

  Future<void> joinConversation(int id) async => _hub?.invoke('JoinConversationGroup', args: [id]);
  Future<void> typing(int id, bool value) async => _hub?.invoke('Typing', args: [id, value]);
  Future<void> dispose() async { await _hub?.stop(); await events.close(); }
}
