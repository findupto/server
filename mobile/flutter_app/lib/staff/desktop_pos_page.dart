import 'package:flutter/material.dart';
import '../core/api_client.dart';

class DesktopPosPage extends StatefulWidget {
  final PosApiClient api;
  const DesktopPosPage({super.key, required this.api});

  @override
  State<DesktopPosPage> createState() => _DesktopPosPageState();
}

class _DesktopPosPageState extends State<DesktopPosPage> {
  final search = TextEditingController();
  final barcode = TextEditingController();
  final cart = <int, Map<String, dynamic>>{};
  List<dynamic> products = [];
  String orderType = 'DineIn';
  String paymentMethod = 'Cash';
  bool loading = true;
  String? error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    try {
      products = await widget.api.products();
    } catch (e) {
      error = e.toString();
    }
    if (mounted) setState(() => loading = false);
  }

  Iterable<Map<String, dynamic>> get filtered {
    final q = search.text.trim().toLowerCase();
    return products.map((e) => Map<String, dynamic>.from(e)).where((p) {
      if (q.isEmpty) return true;
      return '${p['name']} ${p['barcode'] ?? ''}'.toLowerCase().contains(q);
    });
  }

  void add(Map<String, dynamic> p) {
    final id = p['id'] as int;
    setState(() {
      final old = cart[id];
      cart[id] = {
        'productId': id,
        'name': p['name'],
        'price': (p['price'] as num).toDouble(),
        'quantity': (old?['quantity'] ?? 0) + 1,
      };
    });
  }

  void remove(int id) {
    setState(() {
      final item = cart[id];
      if (item == null) return;
      final quantity = item['quantity'] as int;
      if (quantity <= 1) {
        cart.remove(id);
      } else {
        item['quantity'] = quantity - 1;
      }
    });
  }

  double get total => cart.values.fold(0, (s, x) => s + (x['price'] as double) * (x['quantity'] as int));

  Future<void> checkout() async {
    if (cart.isEmpty) return;
    try {
      await widget.api.createOrder(
        items: cart.values.map((x) => {'productId': x['productId'], 'quantity': x['quantity']}).toList(),
      );
      if (!mounted) return;
      setState(cart.clear);
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Sale created')));
    } catch (e) {
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.toString())));
    }
  }

  @override
  void dispose() {
    search.dispose();
    barcode.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    if (loading) return const Scaffold(body: Center(child: CircularProgressIndicator()));
    if (error != null) return Scaffold(body: Center(child: Text(error!)));
    return Scaffold(
      appBar: AppBar(title: const Text('FindUpTo POS'), actions: [
        DropdownButton<String>(value: orderType, items: const ['DineIn', 'Takeaway', 'Delivery'].map((x) => DropdownMenuItem(value: x, child: Text(x))).toList(), onChanged: (v) => setState(() => orderType = v!)),
        const SizedBox(width: 16),
      ]),
      body: Row(children: [
        Expanded(flex: 3, child: Padding(padding: const EdgeInsets.all(16), child: Column(children: [
          TextField(controller: barcode, autofocus: true, decoration: const InputDecoration(labelText: 'Scan barcode', prefixIcon: Icon(Icons.qr_code_scanner)), onSubmitted: (value) {
            final p = products.cast<Map<String, dynamic>?>().firstWhere((x) => x?['barcode'] == value.trim(), orElse: () => null);
            if (p != null) add(p); barcode.clear();
          }),
          const SizedBox(height: 8),
          TextField(controller: search, decoration: const InputDecoration(labelText: 'Search products', prefixIcon: Icon(Icons.search)), onChanged: (_) => setState(() {})),
          const SizedBox(height: 12),
          Expanded(child: GridView.builder(gridDelegate: const SliverGridDelegateWithMaxCrossAxisExtent(maxCrossAxisExtent: 220, childAspectRatio: 1.7), itemCount: filtered.length, itemBuilder: (_, i) {
            final p = filtered.elementAt(i);
            return Card(child: InkWell(onTap: () => add(p), child: Padding(padding: const EdgeInsets.all(12), child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [Text(p['name'], style: Theme.of(context).textTheme.titleMedium), const Spacer(), Text('Rs. ${p['price']}'), const Align(alignment: Alignment.bottomRight, child: Icon(Icons.add_circle_outline))]))));
          })),
        ]))),
        SizedBox(width: 390, child: Card(margin: const EdgeInsets.all(12), child: Column(children: [
          const ListTile(title: Text('Current Sale'), leading: Icon(Icons.receipt_long)),
          const Divider(),
          Expanded(child: ListView(children: cart.values.map((x) => ListTile(title: Text(x['name']), subtitle: Text('${x['quantity']} × Rs. ${x['price']}'), trailing: Wrap(children: [IconButton(onPressed: () => remove(x['productId']), icon: const Icon(Icons.remove)), IconButton(onPressed: () => add(x), icon: const Icon(Icons.add))])).toList())),
          Padding(padding: const EdgeInsets.all(16), child: Column(children: [Text('Total  Rs. ${total.toStringAsFixed(2)}', style: Theme.of(context).textTheme.headlineSmall), const SizedBox(height: 8), DropdownButtonFormField<String>(value: paymentMethod, decoration: const InputDecoration(labelText: 'Payment'), items: const ['Cash', 'Card', 'Online'].map((x) => DropdownMenuItem(value: x, child: Text(x))).toList(), onChanged: (v) => setState(() => paymentMethod = v!)), const SizedBox(height: 12), SizedBox(width: double.infinity, child: FilledButton.icon(onPressed: checkout, icon: const Icon(Icons.point_of_sale), label: const Text('Complete Sale')))])),
        ]))),
      ]),
    );
  }
}
