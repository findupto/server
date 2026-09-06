import 'dart:async';
import 'package:flutter/material.dart';
import '../../core/api_client.dart';
import '../../core/realtime.dart';

class LiveChatPage extends StatefulWidget {
  const LiveChatPage({super.key, required this.api});
  final PosApiClient api;
  @override State<LiveChatPage> createState() => _LiveChatPageState();
}
class _LiveChatPageState extends State<LiveChatPage> {
  late final PosRealtime realtime;
  StreamSubscription? sub;
  final input = TextEditingController();
  List<dynamic> messages = [];
  int? conversationId;
  bool connecting = true;

  @override void initState() { super.initState(); realtime = PosRealtime(widget.api); _start(); }
  Future<void> _start() async {
    try {
      final cs = await widget.api.conversations();
      if (cs.isNotEmpty) { conversationId = cs.first['id']; messages = await widget.api.messages(conversationId!); }
      await realtime.connect();
      if (conversationId != null) await realtime.joinConversation(conversationId!);
      sub = realtime.events.stream.listen((event) async { if (event['type'] == 'message.created' && conversationId != null) { messages = await widget.api.messages(conversationId!); if (mounted) setState(() {}); } });
    } finally { if (mounted) setState(() => connecting = false); }
  }
  Future<void> send() async { final value = input.text.trim(); if (value.isEmpty || conversationId == null) return; await widget.api.sendMessage(conversationId!, value); input.clear(); }
  @override void dispose() { sub?.cancel(); realtime.dispose(); input.dispose(); super.dispose(); }
  @override Widget build(BuildContext context) => Scaffold(appBar: AppBar(title: const Text('Live Counter Chat')), body: connecting ? const Center(child: CircularProgressIndicator()) : Column(children: [Expanded(child: ListView.builder(itemCount: messages.length, itemBuilder: (_, i) { final m = messages[i]; return ListTile(title: Text(m['text'] ?? ''), subtitle: Text(m['senderUsername'] ?? '')); })), SafeArea(child: Row(children: [Expanded(child: TextField(controller: input, onChanged: (_) => conversationId == null ? null : realtime.typing(conversationId!, true), decoration: const InputDecoration(hintText: 'Type a message'))), IconButton(onPressed: send, icon: const Icon(Icons.send))]))]));
}
