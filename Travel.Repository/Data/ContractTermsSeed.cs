using Microsoft.EntityFrameworkCore;
using Travel.Domain.Enums;
using Travel.Domain.Models;

namespace Travel.Repository.Data
{
    /// <summary>
    /// Seeds Setup ▸ Contract Terms with the company's standard text — the 2026 CCH contract, without
    /// clause 1.2 (late-payment fine) — so the page isn't blank on day one.
    ///
    /// Guarded on the <see cref="ContractSettings"/> row, not on the sections: an operator who deletes
    /// every section on purpose must not see them come back on the next restart.
    ///
    /// The text is the sample's wording. Only the layout was repaired: paragraphs Word had run together
    /// (5.3 C–E and the Remarcação list) are split, and the party paragraph's dummy values became
    /// placeholders.
    /// </summary>
    public static class ContractTermsSeed
    {
        public static async Task SeedAsync(TravelContext context)
        {
            if (await context.ContractSettings.AnyAsync())
                return;

            context.ContractSettings.Add(new ContractSettings
            {
                DocumentTitle = "CONTRATO DE PRESTAÇÃO DE SERVIÇOS DE TURISMO MUSTA TRAVEL",
                SignatureCity = "São Paulo",
                CompanySignatureName = "MUSTA TRAVEL",
            });

            var order = 0;
            void Add(ContractTermsPlacement placement, string? heading, string body) =>
                context.ContractTermsSections.Add(new ContractTermsSection
                {
                    Placement = placement,
                    SortOrder = order++,
                    Heading = heading,
                    Body = body.Trim(),
                });

            Add(ContractTermsPlacement.Opening, null, """
                {Contratante}, portador do CPF nº {CPF}, RG nº {RG}, residente e domiciliado na {Endereco}, doravante denominado, CONTRATANTE; MUSTA TRAVEL VIAGEM LTDA, pessoa jurídica de direito privado, sediada em ALAMEDA TERRACOTA, 185 - SALA 1213. BAIRRO CERÂMICA, SÃO CAETANO DO SUL / SP inscrita no CNPJ nº 50.709.342/0001-53, e, representada na forma de seu contrato social, doravante denominada CONTRATADA.

                A parte acima identificada tem, entre si, justo e acordado o presente Contrato de Prestação de Serviços de Turismo, que se regerá pelas cláusulas e condições descritas no presente instrumento.
                """);

            Add(ContractTermsPlacement.ObjectClause, "CLÁUSULA PRIMEIRA – DO OBJETO DO CONTRATO", """
                1.0 O presente contrato tem por objeto a prestação, pela CONTRATADA, à CONTRATANTE, dos serviços na área de turismo consistentes:
                Na aquisição de programas de viagens, que podem ser organizados pela CONTRATADA e/ou terceiros;
                Na reserva e no pagamento de vagas em meios de hospedagem;
                De transporte;
                Na contratação de serviços de recepção, transferência e assistência, tudo segundo as especificações descritas no contrato;
                """);
            Add(ContractTermsPlacement.ObjectClause, null, "1.1 A VIAGEM SEGUIRÁ O SEGUINTE CRONOGRAMA:");

            Add(ContractTermsPlacement.Closing, null, """
                1.3 No caso de cartão de crédito o cliente envia o pagamento e se não for aprovado, os valores poderão sofrer alterações decorrentes de variações cambiais e/ou resoluções governamentais, da IATA ou ANAC que comprovadamente os altere. Essas alterações serão informadas antes do pagamento novamente caso ocorra. No caso de grupos por adesão, a alteração de preços é atrelada ao número de participantes, informação esta que deverá constar do acordo específico. Como também os preços podem ser alterados de um cliente para o outro dependendo do dia da compra, da variação cambial do dia real x dólar americano, disponibilidade na data e variação cambial da moeda do destino em relação ao dólar americano como também condições promocionais facultando a operadora decidir qual preço pratica ao cliente.
                """);

            Add(ContractTermsPlacement.Closing, "CLÁUSULA SEGUNDA - OBRIGAÇÕES DA CONTRATADA", """
                2.0 É de responsabilidade da CONTRATADA, o fiel cumprimento de todos os serviços incluídos neste pacote tal como ajustados neste contrato, restando consignado que a CONTRATADA é intermediária na contratação de serviços turísticos, ficando na dependência de terceiros para a efetiva execução de tais serviços (transporte, hospedagem, atendimento receptivo no local de destino, terceirização de guias turísticos com cadastro na Embratur, entre outros serviços diversos), respondendo cada um na medida de sua atuação.

                2.1 A CONTRATADA providenciará em até 5 dias antes do embarque, a emissão dos documentos de viagem (voucher), entregando toda a referida documentação para o CONTRATANTE, além de fornecer todas as informações necessárias sobre a viagem contratada.

                2.2 A CONTRATADA em parceria e colaboração com seus fornecedores, reserva-se o direito de promover alterações que se fizerem necessárias quanto aos itinerários, hotéis, serviços, etc., sem prejuízo para o passageiro. Caso necessário, poderá também alterar a data de embarque, a fim de garantir o transporte aéreo, limitando essas alterações a um dia a mais ou a menos da data original, informando o CONTRATANTE sobre a alteração e dando-lhe a opção de aceitar a mesma ou cancelar sua reserva com respectivo reembolso como crédito. Como também a CONTRATADA se reserva o direito de alterar os itinerários, hotéis, serviços, etc. por motivo de segurança ou caso de acidente de terceiros que impede a execução do itinerário conforme combinado desde que seja sem prejuízo ao passageiro.

                2.3 A CONTRATADA não se responsabiliza por qualquer atraso de voo, alterações de equipamento, aeroportos, horários, rotas, escalas, conexões, alternâncias entre voos regulares/fretados e vice-versa, acidentes, perda, avaria ou extravio de bagagem ou qualquer outro dano causado ao CONTRATANTE durante ou em decorrência do transporte aéreo, sendo certo que a responsabilidade será exclusiva da companhia aérea responsável pelo transporte, de acordo com as normas internacionais (Convenção de Montreal) e o Código Brasileiro de Aeronáutica, principalmente quando estes incidentes tiverem por fim preservar a segurança de voo; atender à questões técnico-operacionais, acatar limitações climáticas, decorrer de fechamento de aeroportos por segurança ou, ainda, por determinação do comandante da aeronave.

                2.4 Em caso de efetiva ameaça de ocorrência de fenômenos da natureza com possíveis riscos aos participantes, situação de calamidade pública, perturbação da ordem, guerras, acidentes ou de greves prejudiciais aos serviços de viagem, poderá a CONTRATADA cancelar a viagem, ou parte desta, antes de seu início, ou em qualquer fase ou etapa, devendo a CONTRATADA restituir os valores correspondentes aos serviços não utilizados, desde que não tenham sido faturados ou que tenham sido estornados pelos hotéis e serviços de receptivo, sem acréscimo de multa, juros ou qualquer outro encargo.

                2.5 No caso da ocorrência de casos fortuitos ou de força maior no Egito (fenômenos naturais, cataclismos, terremotos, furacões, ciclones, inundações, etc.) bem como de levantes sociais (protestos públicos, revoluções, atos terroristas, etc.), a CONTRATADA não se responsabiliza pelos danos materiais ou morais decorrentes, não cabendo nenhum tipo de indenização, conforme o disposto no artigo 393 do Código Civil.
                """);

            Add(ContractTermsPlacement.Closing, "CLÁUSULA TERCEIRA - OBRIGAÇÕES DA CONTRATANTE", """
                3.1. Os passageiros deverão, sob sua responsabilidade, providenciar e estar ciente de:

                A. Apresentar-se no aeroporto até 03 (três) horas antes do horário previsto para embarque,
                B. Reconfirmar diretamente na companhia aérea cada voo subsequente com antecedência mínima de 72 (setenta e duas) horas da saída do voo;
                C. O transportador não poderá retardar um vôo para aguardar passageiros porventura retidos por autoridades fiscais ou policiais para fiscalização;
                D. O não embarque caracterizará cancelamento da viagem e o CONTRATANTE sofrerá perda total do pacote;
                E. Passageiros que dependam do voo de conexão devem realizá-la com intervalo mínimo de 03 (três) horas. Quando isso se der em aeroportos diferentes, o lapso de tempo deve ser de, no mínimo, 05 (cinco) horas;
                F. Os embarques só serão feitos mediante documentação original dependendo do destino (RG original com foto) ou passaporte válido (mínimo de 06 meses de validade), conforme exigências do destino de viagem e de total responsabilidade do contratante providenciar a tempo os documentos necessários para a viagem. Menores desacompanhados precisam de autorização com firmas reconhecidas do pai e da mãe, além do RG original e/ou passaporte;
                G. Apresentar documentação pessoal (RG original, certidão de nascimento original, RNE original em caso de estrangeiro, passaporte válido, vistos consulares, vacinas, etc.). A impossibilidade de embarque gerada por falta de documentação caracterizará cancelamento da viagem e o CONTRATANTE sofrerá perda total do pacote contratado;
                H. Para menores de 12 anos viajando desacompanhados, além dos documentos acima, será exigido RG original, uma autorização do pai e da mãe com firma reconhecida. No caso de o menor estar em companhia apenas de um dos pais, será necessário a autorização do ausente.
                I. Nas viagens internacionais não serão aceitos os seguintes documentos: fotocópia de documentos (mesmo que sejam autenticados), documentos militares ou registros de exercício profissional, documentos com sobrenome diferente do emitido na passagem aérea, certidão de nascimento (mesmo para menores);
                J. Se o CONTRATANTE, no decorrer da viagem, necessitar de assistência médica ou remédios, deverá suportar tais encargos. A CONTRATADA orienta para que os titulares de seguro saúde ou assistência médica portem consigo os documentos necessários para atendimento fora do domicílio habitual. A contratada não se responsabiliza caso o cliente viaje sem o seguro de saúde com boa cobertura dependendo do destino, como também não se responsabiliza em caso de falecimento de trazer o corpo para a origem da viagem.
                """);

            Add(ContractTermsPlacement.Closing, "CLÁUSULA QUARTA – CONDIÇÕES GERAIS", """
                4.1 No caso de o CONTRATANTE e o passageiro não se tratarem da mesma pessoa, compromete-se aquele a levar ao conhecimento deste o teor deste contrato, sendo solidariamente responsável por qualquer ato deste último praticado no âmbito da execução do contrato.

                4.2 A restrição ao ingresso ou permanência do CONTRATADO em território internacional se dará por única e exclusiva decisão das autoridades locais, que exercem o total controle de suas fronteiras, com o poder de autorizar ou não a entrada, permanência e saída de bens e pessoas de seu território. Trata-se de direito ligado à soberania de cada Estado, reconhecido pela comunidade e normas internacionais.

                4.3 No caso de alteração de data ou horário de voo, acidentes, perda ou extravio de bagagem, fica previamente estabelecido que a responsabilidade será exclusiva da companhia aérea em questão e de acordo com normas internacionais (Convenção de Varsóvia) e o Código Brasileiro de Aeronáutica.

                4.4 A realização de escalas técnicas ficará a critério do Comandante da aeronave.

                4.5 Não nos responsabilizamos por serviços adquiridos fora do Brasil ou com outra operadora.

                4.6 Gorjetas cobradas no destino: é uma cultura local e serão cobrados referente ao serviço de motorista, guia e maleteiros, sendo paga para o guia local no destino. Não efetuamos reembolso de gorjetas pagas localmente.

                4.7 Em caso de viagens para uma data fixa de grupos é necessário que tenha no mínimo 2 passageiros para que seja dado andamento às reservas, caso não tenha a quantidade mínima de pessoas será transferido à viagem para o próximo grupo disponível sem custos.

                4.8 Quanto à marcação de assentos e check-in, a liberação da marcação será gratuita somente durante o check-in online ou no aeroporto, dependendo da restrição de cada companhia aérea e bilhete. Bilhetes de grupo poderão ter o check-in liberado somente no aeroporto. Voos codeshare (operados por cias aéreas diferentes da mandatória do bilhete) terão check-in e marcação de assentos liberados somente no aeroporto. Para marcação antecipada, a cia aérea poderá cobrar valores por pessoa e por voo. Assentos na 1ª fileira são bloqueados e só liberados no balcão da cia no aeroporto, visando prioridade a gestantes, idosos, crianças desacompanhadas e casos de necessidades médicas. Consultas a disponibilidade no dia do embarque, no aeroporto.

                4.9 Alterações de voos e horários pela cia aérea: Qualquer alteração de voo realizada após o bilhete já emitido, antes ou durante a viagem, a cia aérea ficará responsável pela modificação e prestação de eventuais atendimentos. Se houver pernoites adicionais, custos com hospedagens, traslados e extravios de bagagem, guardar todas as notas fiscais originais e verificar diretamente no balcão da cia aérea no aeroporto de embarque e/ou desembarque o procedimento.

                4.10 Em caso de atraso e/ou perda de voo resultar na perda de serviços ou pernoites, a responsabilidade da operadora limita-se a sempre e somente quando for possível, reajustar o programa para oferecer tours perdidos nos dias seguintes, mediante pagamento de serviço no local, pelo passageiro, sem reembolso.

                4.11 Quando traslados e passeios descritos no roteiro forem regulares de turismo, serão serviços compartilhados com outros passageiros, e serão realizados em veículo de tamanho proporcional ao número de pessoas. Os traslados e transportes poderão ter ou não guia (conforme pacote contratado). Os passeios contratados serão realizados em dias definidos pelo receptivo local, salvo solicitação de dias específicos incluídos no contrato. Para qualquer mudança de data, poderá ser cobrado valores adicionais.

                4.12 Exceto se expressamente mencionado no voucher, os passeios opcionais não estão inclusos no preço contratado, não tendo a CONTRATADA qualquer responsabilidade quanto à sua contratação e execução. A CONTRATADA recomenda que os serviços opcionais sejam adquiridos pela própria Contratada para que garanta o máximo de eficiência em questões de reservas, prazos e eficiência do serviço.

                4.13 Exceto se expressamente mencionado no Termo Inclusos, os serviços que não estão inclusos no preço contratado, não tendo a CONTRATADA qualquer responsabilidade quanto à sua contratação e execução. Mesmo que esteja escrito no roteiro, a CONTRATADA recomenda que o passageiro revise sempre os itens de “incluído” e “não incluído” pois é isso que vale , o roteiro é apenas uma referência.

                4.14 O nome do hotel, o tipo de acomodação adquirida e demais peculiaridades do pacote turístico adquirido, devem constar no voucher. Para a categoria do apartamento diferenciada daquela adquirida e/ou localização especial, caberá ao CONTRATANTE tratar diretamente com o hotel a disponibilidade e preço, sob suas expensas. Cabe ao contratante analisar os hotéis descritos no pacote adquirido verificando se está de acordo com as avaliações, localização e peculiaridades do hotel confirmado.

                4.15 Check-in e check-out no hotel: As reservas de hotéis são feitas para entrada e saída nos dias contratados. Em caso de comparecimento no hotel após o primeiro dia contratado da hospedagem, sem comunicação antecipada e aviso prévio, o hotel poderá considerar como no-show (não comparecimento) e cancelar os quartos reservados, sem devolução de valores. No check-in, dependendo das regras turísticas de cada região, o hotel poderá cobrar a taxa turística por noite/quarto, imposta pelo governo local e/ou taxas de consumo antecipadas, para serem abatidas em caso de utilização de serviços adicionais do hotel como bebidas e refeições, e devolvidos se não houver tais consumos. As taxas turísticas não são devolvidas.

                4.16 Em caso de dieta alimentar diferenciada, o CONTRATANTE deverá informar e consultar previamente o local (hotel, pousada, restaurante) sobre a possibilidade de atendimento, e, em regra, quando possível, implica no pagamento de taxa adicional.

                4.17 Em caso de cobrança de taxas governamentais ou municipais localmente no hotel, fica de total responsabilidade do cliente pagar para o hotel. Não efetuamos reembolso de taxas pagas localmente.

                4.18 Alguns destinos internacionais exigem que os passageiros contratem assistência de viagem previamente. Essa informação constará das especificações contratuais e, portanto, deverá o CONTRATANTE adquirir a apólice correspondente. Para viagens no território brasileiro, a contratação de cartões de assistência de viagem é opcional e os serviços poderão ser adquiridos ou não por intermédio da contratada e, também, constarão, expressamente, no presente instrumento. Cabe ao CONTRATANTE identificar e adquirir o programa de assistência de viagem com a cobertura mais adequada às suas necessidades.

                4.19 Em Caso do passageiro sofrer qualquer negação de visto ou Negação de entrada ao País por motivo de segurança , problemas políticos (como em casos em que algum país não permita a entrada de um cliente que já visitou um país rival), por algum motivo que o país negue a entrada do Passageiro , isenta a CONTRATADA de qualquer responsabilidade e nesse caso a CONTRATADA daria uma opção de aceitar alterar sua viagem ou cancelar sua reserva com respectivo reembolso como crédito após calcular as multas . Como também a Contratada se reserva o direito de alterar itinerários, hotéis, serviços, etc.

                4.20 A bagagem e demais itens pessoais do passageiro não são objetos deste contrato. A CONTRATADA não se responsabiliza pela perda, roubo, extravio ou danos que as bagagens possam sofrer durante a viagem, por qualquer causa, incluindo sua manipulação em traslados, quando este serviço existir. Na hipótese de sofrer algum dano ou extravio, o passageiro deve apresentar, no ato, reclamação para a companhia de transporte.

                4.21 A CONTRATADA não se responsabiliza por roubo de documentos, objetos de valor e pessoais durante a viagem. Recomenda-se verificar junto ao hotel a existência de cofres para a guarda desses objetos.

                4.22 As condições específicas para reservas, pagamentos, alterações, cancelamentos, reembolso e documentação para cruzeiros fazem parte de documento próprio da companhia de cruzeiros. A CONTRATADA nos programas oferecidos mediante transporte marítimo ou de passeios aquaviários, somente contrata navios ou barcos reconhecidos pelos órgãos competentes como apropriados à realização desses tipos de transporte e obedecida à legislação. É de exclusiva responsabilidade das empresas que prestam serviços de transporte marítimo de pessoas ou de passeios aquaviários, o devido cumprimento das leis e das normas regulamentares aplicáveis a essa atividade, inclusive da devida cobertura de seguros.

                4.23 O CONTRATANTE deverá observar atentamente aos serviços oferecidos pelas embarcações que não estão incluídos na tarifa do cruzeiro, bem como o regulamento interno, que disponibiliza os horários e programas oferecidos.

                4.24 O passageiro e/ou turista que estiver usufruindo do contrato poderá ter impedido o início ou continuidade de sua viagem nas seguintes hipóteses: se causar perturbação ou se sua presença oferecer risco à saúde, à integridade física ou moral de quem quer que seja, circunstâncias estas que serão apreciadas pelas pessoas competentes, não lhe sendo devida qualquer restituição de valores pagos.

                4.25 Em caso de reclamações quanto à prestação dos serviços, o CONTRATANTE deverá encaminhar por escrito à CONTRATADA em até 48 (quarenta e oito) horas após o encerramento dos mesmos. Se não o fizer dentro do prazo estipulado, a relação contratual será considerada perfeita e concluída.

                4.26 O não exercício pelas partes de quaisquer dos direitos ou prerrogativas previstas neste contrato ou na legislação aplicável, será tido como ato de mera liberalidade, não constituindo alteração ou novação das obrigações ora estabelecidas, cujo cumprimento poderá ser exigido a qualquer tempo, independentemente de comunicação prévia à outra parte.
                """);

            Add(ContractTermsPlacement.Closing, "CLÁUSULA QUINTA - DA RESCISÃO, CANCELAMENTO E MULTA E REMARCAÇÃO", """
                5.1 Este contrato ficará rescindido a partir do momento em que qualquer uma das partes fizer comunicação por escrito à parte contrária sobre a rescisão.

                5.2 Entende-se por cancelamento, a desistência da viagem e/ou do serviço contratado, bem como alteração de datas.

                5.3 Solicitações de cancelamento da viagem ou serviços contratados deverão ser feitas por escrito (e-mail ou whatsapp) e, quando recebidos após a confirmação dos serviços, terão, em todos os casos, taxas por gastos de comunicação variáveis conforme as regras estabelecidas por terceiros envolvidos, tais como operadores, hotéis, companhias aéreas, etc. por cada reserva. As penalidades serão aplicadas segundo a antecedência com que forem comunicadas ou de acordo com as condições de produtos específicos, informadas no ato da confirmação. Caso não tenha nenhuma indicação expressa na confirmação, a regra padrão para cálculo do valor das multas (além da taxa de comunicação), e a seguinte:

                A. Cancelamento com mais de 90 dias de antecedência: Será devolvido o valor total do pacote , descontando uma taxa administrativa de 20% para cobrir custos operacionais e taxas de reserva.
                B. Cancelamento entre 89 e 30 dias antes da viagem: Será retido 50% do valor total do pacote , devido a compromissos financeiros já assumidos com fornecedores.
                C. Cancelamento com menos de 30 dias antes da viagem: Será retido 80% do valor total do pacote , considerando que os serviços já foram reservados e os valores pagos aos fornecedores não são reembolsáveis
                D. Cancelamento após o início dos serviços contratados: Considera-se perda total do valor do pacote.
                E. Período de arrependimento (7 dias corridos após a compra): Dentro de 7 dias corridos após a aquisição do pacote, o valor pago será devolvido integralmente.

                Política de Remarcação
                1. Remarcação com mais de 60 dias de antecedência: É permitida a remarcação da viagem para outra data disponível, sem cobrança de multa. Serão aplicados os reajustes de valores relacionados a tarifas aéreas, hospedagens e outros serviços conforme a nova data escolhida.
                2. Remarcação entre 59 e 30 dias antes da viagem: Será cobrada uma taxa administrativa de 10% do valor total do pacote para cobrir os custos de alteração e diferenças tarifárias dos fornecedores.
                3. Remarcação com menos de 30 dias antes da viagem: A remarcação estará sujeita a uma taxa de 20% do valor total do pacote, além de possíveis custos adicionais cobrados pelos fornecedores. A solicitação estará sujeita à disponibilidade na nova data escolhida.
                4. Remarcação por motivos de força maior (doença, óbito, etc.): Mediante apresentação de documentação comprobatória, a remarcação poderá ser realizada sem multa administrativa, mas estará sujeita à disponibilidade e às políticas dos fornecedores.
                5. Remarcação durante o período promocional: Para pacotes adquiridos em promoções ou com condições especiais, a remarcação estará sujeita à perda de benefícios promocionais e aplicação de tarifas regulares para a nova data.

                5.4 Quando a execução dos serviços adquiridos não puder ser realizada por motivo técnico ou por impedimento da prestadora de serviço de hotelaria, reserva-se a CONTRATADA o direito de cancelar a prestação de serviço de viagem comunicando o CONTRATANTE com a antecedência mínima de 72 (setenta e duas) horas. Ocorrendo o cancelamento, ficará a escolha do CONTRATANTE a contratação de outra prestadora do serviço de hospedagem ou a programação para outra data. Não optando por nenhuma das possibilidades, será devolvido pela CONTRATADA integralmente o valor pago como crédito.
                """);

            Add(ContractTermsPlacement.Closing, "CLÁUSULA SEXTA – DA PUBLICIDADE", """
                6.1 Pelo presente instrumento particular e na melhor forma de direito, o CONTRATANTE autoriza a CONTRATADA, desde já, em caráter irretratável e irrevogável, a:
                (a) utilizar e veicular as fotografias e vídeos realizados com o registro da imagem do(a) CONTRATANTE no “folder” sobre a CONTRATADA, para fins de publicidade institucional e/ou de produtos, sem qualquer limitação de número de inserções e reproduções;
                (b) utilizar e veicular as fotografias e vídeos acima referidas no site da CONTRATADA na Internet, nas redes Intranet e Extranet, do (a) CONTRATANTE (redes de computadores restrita aos seus funcionários, fornecedores e empresas franqueadas);
                (c) utilizar as fotografias e vídeos na produção de quaisquer materiais publicitários e promocionais para fins de divulgação do “folder” da CONTRATADA, tais como, exemplificativamente, anúncios em revistas e jornais, folhetos, cartazes, “posters”, filmes publicitários, “outdoor” e “busdoor”, dentre outros, a serem veiculados, no Brasil e/ou no exterior, em quaisquer veículos, formatos e mídia (televisão, cinema, mídias impressa, mídias sociais, etc.), sem limitação de número de inserções e reproduções;
                (d) utilizar as fotografias e vídeos para a produção de materiais publicitários institucionais da CONTRATADA a serem veiculados nas mesmas condições previstas na alínea anterior; e
                (e) utilizar as fotografias e vídeos em veículos próprios da CONTRATADA, tais como jornais, redes sociais, manuais de treinamento de funcionários, boletins, catálogos e quaisquer outros materiais a serem distribuídos entre seus funcionários, fornecedores e empresas franqueadas.
                """);

            Add(ContractTermsPlacement.Closing, "CLÁUSULA SÉTIMA – DO FORO", """
                7.1. Fica eleito o Foro da Comarca de São Paulo/SP, para dirimir quaisquer dúvidas decorrentes do contrato ora celebrado, renunciando a qualquer outro por mais privilegiado que seja.

                E, por estarem assim justas e contratadas, assinam as partes o presente contrato, em duas vias de igual teor, na presença das testemunhas abaixo qualificadas, para um só fim e efeito de direito.
                """);

            Add(ContractTermsPlacement.Closing, "CLÁUSULA ADICIONAL – ACOMODAÇÃO EM QUARTOS DUPLOS", """
                O CONTRATANTE declara estar ciente de que os valores cobrados no presente contrato referem-se a acomodação em quartos duplos, ou seja, compartilhados com outro viajante do mesmo gênero.

                A CONTRATADA compromete-se a realizar o encaixe do CONTRATANTE com outro viajante de mesmo gênero para composição da acomodação dupla.

                Caso não haja outro viajante disponível no grupo para compartilhamento do quarto, o CONTRATANTE compreende que será necessário arcar com um custo adicional de 35% (trinta e cinco por cento) sobre o valor total do roteiro contratado, correspondente à adaptação para acomodação individual.

                O CONTRATANTE será previamente informado pela CONTRATADA sobre a necessidade dessa cobrança adicional, para que possa decidir pela manutenção do roteiro nas condições ajustadas
                """);

            await context.SaveChangesAsync();
        }
    }
}
