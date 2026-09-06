import 'package:flutter/material.dart';
import '../../core/api_client.dart';

class CounterOrdersPage extends StatefulWidget {
  const CounterOrdersPage({super.key, required this.api});
  final PosApiClient api;
  @override State<CounterOrdersPage> createState() => _CounterOrdersPageState();
}

class _CounterOrdersPageState extends State<CounterOrdersPage> {
  List<dynamic> orders = [];
  bool loading = true;
  String? error;
  @override void initState() { super.initState(); load(); }
  Future<void> load() async { try { final v = await widget.api.getList('/api/orders'); if (mounted) setState(() { orders = v; loading = false; }); } catch (e) { if (mounted) setState(() { error = e.toString(); loading = false; }); } }

  Future<void> pay(Map<String,dynamic> order) async {
    final controller = TextEditingController(text: '${order['total'] ?? 0}');
    final value = await showDialog<String>(context: context, builder: (_) => AlertDialog(title: Text('Pay order #${order['id']}'), content: TextField(controller: controller, keyboardType: const TextInputType.numberWithOptions(decimal: true), decoration: const InputDecoration(labelText: 'Cash received')), actions: [TextButton(onPressed: () => Navigator.pop(context), child: const Text('Cancel')), FilledButton(onPressed: () => Navigator.pop(context, controller.text), child: const Text('Collect'))]));
    if (value == null) return;
    try { final amount = double.parse(value); await widget.api.post('/api/orders/${order['id']}/payment', {'amountTendered': amount, 'method': 'Cash', 'reference': ''}); if (mounted) { ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Payment collected'))); load(); } } catch (e) { if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.toString()))); }
  }

  @override Widget build(BuildContext context) => Scaffold(appBar: AppBar(title: const Text('Counter Orders'), actions: [IconButton(onPressed: load, icon: const Icon(Icons.refresh))]), body: loading ? const Center(child: CircularProgressIndicator()) : error != null ? Center(child: Text(error!)) : RefreshIndicator(onRefresh: load, child: ListView.builder(itemCount: orders.length, itemBuilder: (_, i) { final o = Map<String,dynamic>.from(orders[i]); final paid = o['status'] == 'Paid' || o['paymentStatus'] == 'Paid'; return Card(child: ListTile(title: Text('Order #${o['id']} • ${o['orderType'] ?? 'Counter'}'), subtitle: Text('${o['status']} • Rs. ${o['total']}'), trailing: paid ? const Icon(Icons.check_circle) : FilledButton(onPressed: () => pay(o), child: const Text('Pay'))); }));
}
