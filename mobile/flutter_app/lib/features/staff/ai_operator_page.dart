import 'package:flutter/material.dart';
import '../../core/api_client.dart';

class AiOperatorPage extends StatefulWidget {
  const AiOperatorPage({super.key, required this.api, required this.role});
  final PosApiClient api;
  final String role;
  @override State<AiOperatorPage> createState() => _AiOperatorPageState();
}

class _AiOperatorPageState extends State<AiOperatorPage> {
  final controller = TextEditingController();
  final messages = <Map<String, String>>[];
  bool busy = false;

  Future<void> send() async {
    final text = controller.text.trim();
    if (text.isEmpty || busy) return;
    controller.clear();
    setState(() { messages.add({'role': 'You', 'text': text}); busy = true; });
    try {
      final result = await widget.api.aiOperate(text);
      setState(() => messages.add({'role': 'AI', 'text': (result['message'] ?? 'Done').toString()}));
    } catch (e) {
      setState(() => messages.add({'role': 'AI', 'text': 'Could not complete the operation: $e'}));
    } finally { if (mounted) setState(() => busy = false); }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: Text('AI Business Operator • ${widget.role}')),
    body: Column(children: [
      Expanded(child: ListView.builder(padding: const EdgeInsets.all(12), itemCount: messages.length, itemBuilder: (_, i) {
        final m = messages[i];
        return Align(alignment: m['role'] == 'You' ? Alignment.centerRight : Alignment.centerLeft, child: Card(child: Padding(padding: const EdgeInsets.all(12), child: Text('${m['role']}: ${m['text']}'))));
      })),
      if (busy) const LinearProgressIndicator(minHeight: 2),
      Padding(padding: const EdgeInsets.fromLTRB(12, 8, 12, 12), child: Row(children: [
        Expanded(child: TextField(controller: controller, minLines: 1, maxLines: 4, onSubmitted: (_) => send(), decoration: const InputDecoration(hintText: 'e.g. Sell 2 Zinger Burger for cash, add 20 Coke stock, show today sales…', border: OutlineInputBorder())),),
        const SizedBox(width: 8),
        IconButton.filled(onPressed: busy ? null : send, icon: const Icon(Icons.send)),
      ])),
    ]),
  );
}
